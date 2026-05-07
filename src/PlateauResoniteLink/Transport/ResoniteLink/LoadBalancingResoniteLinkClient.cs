using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Logging;

using ResoniteLink;

namespace PlateauResoniteLink.Transport.ResoniteLink;

internal sealed class LoadBalancingResoniteLinkClient : IResoniteLinkClient
{
    private const int SessionStateRouteIndex = 0;

    private readonly object routeLock = new();
    private readonly IResoniteLinkClient[] clients;
    private readonly int[] activeOperationCounts;
    private readonly Action<string>? reportProgress;
    private int routeCursor;
    private int disposed;

    public LoadBalancingResoniteLinkClient(IReadOnlyList<IResoniteLinkClient> clients, Action<string>? reportProgress = null)
    {
        this.clients = clients is null ? throw new ArgumentNullException(nameof(clients)) : [.. clients];
        this.reportProgress = reportProgress;
        if (this.clients.Length == 0)
        {
            throw new ArgumentException("At least one client must be configured for load balancing.", nameof(clients));
        }

        activeOperationCounts = new int[this.clients.Length];
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        // Routing client is a pure dispatch layer; caller owns underlying client lifetimes.
    }

    public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        return ConnectAllRoutesAsync(endpoint, cancellationToken);
    }

    public Task<ResoniteTransportComponentCreationResult> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
    {
        return ExecuteOnSessionStateRouteAsync("add_component", (client, ct) => client.AddComponentAsync(request, ct), cancellationToken);
    }

    public Task<ResoniteTransportSlotCreationResult> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
    {
        return ExecuteOnSessionStateRouteAsync("add_slot", (client, ct) => client.AddSlotAsync(request, ct), cancellationToken);
    }

    public Task<BatchResponse> RunDataModelOperationBatchAsync(
        IReadOnlyList<DataModelOperation> operations,
        CancellationToken cancellationToken)
    {
        return ExecuteOnSessionStateRouteAsync(
            "run_data_model_operation_batch",
            (client, ct) => client.RunDataModelOperationBatchAsync(operations, ct),
            cancellationToken);
    }

    public Task<Slot?> GetSlotAsync(ResoniteTransportSlotLocator slot, int depth, CancellationToken cancellationToken)
    {
        return ExecuteOnSessionStateRouteAsync("get_slot", (client, ct) => client.GetSlotAsync(slot, depth, ct), cancellationToken);
    }

    public Task<Component?> GetComponentAsync(ResoniteTransportComponentLocator component, CancellationToken cancellationToken)
    {
        return ExecuteOnSessionStateRouteAsync(
            "get_component",
            (client, ct) => client.GetComponentAsync(component, ct),
            cancellationToken);
    }

    public Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
    {
        return ExecuteOnLeastBusyRouteAsync("import_mesh", (client, ct) => client.ImportMeshAsync(request, ct), cancellationToken);
    }

    public Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
    {
        return ExecuteOnLeastBusyRouteAsync(
            "import_texture",
            (client, ct) => client.ImportTextureAsync(textureImport, ct),
            cancellationToken);
    }

    public Task UpdateComponentAsync(ResoniteComponentUpdate request, CancellationToken cancellationToken)
    {
        return ExecuteOnSessionStateRouteAsync("update_component", (client, ct) => client.UpdateComponentAsync(request, ct), cancellationToken);
    }

    private Task ExecuteOnSessionStateRouteAsync(
        string operationName,
        Func<IResoniteLinkClient, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        return ExecuteOnRouteAsync(operationName, SessionStateRouteIndex, operation, cancellationToken);
    }

    private Task<TResult> ExecuteOnSessionStateRouteAsync<TResult>(
        string operationName,
        Func<IResoniteLinkClient, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        return ExecuteOnRouteAsync(operationName, SessionStateRouteIndex, operation, cancellationToken);
    }

    private async Task ExecuteOnLeastBusyRouteAsync(
        string operationName,
        Func<IResoniteLinkClient, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        RouteLease routeLease = AcquireLeastBusyRoute(operationName);
        try
        {
            await operation(clients[routeLease.RouteIndex], cancellationToken);
        }
        finally
        {
            ReleaseRoute(routeLease);
        }
    }

    private async Task ExecuteOnRouteAsync(
        string operationName,
        int routeIndex,
        Func<IResoniteLinkClient, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        RouteLease routeLease = AcquireRoute(operationName, routeIndex);
        try
        {
            await operation(clients[routeLease.RouteIndex], cancellationToken);
        }
        finally
        {
            ReleaseRoute(routeLease);
        }
    }

    private async Task<TResult> ExecuteOnRouteAsync<TResult>(
        string operationName,
        int routeIndex,
        Func<IResoniteLinkClient, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        RouteLease routeLease = AcquireRoute(operationName, routeIndex);
        try
        {
            return await operation(clients[routeLease.RouteIndex], cancellationToken);
        }
        finally
        {
            ReleaseRoute(routeLease);
        }
    }

    private async Task<TResult> ExecuteOnLeastBusyRouteAsync<TResult>(
        string operationName,
        Func<IResoniteLinkClient, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        RouteLease routeLease = AcquireLeastBusyRoute(operationName);
        try
        {
            return await operation(clients[routeLease.RouteIndex], cancellationToken);
        }
        finally
        {
            ReleaseRoute(routeLease);
        }
    }

    private RouteLease AcquireLeastBusyRoute(string operationName)
    {
        lock (routeLock)
        {
            int selectedRouteIndex = 0;
            int selectedOperationCount = int.MaxValue;
            int startRouteIndex = routeCursor;
            for (int offset = 0; offset < clients.Length; offset++)
            {
                int routeIndex = (startRouteIndex + offset) % clients.Length;
                int operationCount = activeOperationCounts[routeIndex];
                if (operationCount < selectedOperationCount)
                {
                    selectedRouteIndex = routeIndex;
                    selectedOperationCount = operationCount;
                }
            }

            activeOperationCounts[selectedRouteIndex]++;
            routeCursor = (selectedRouteIndex + 1) % clients.Length;
            Report(
                PlateauLog.Debug(
                    "live",
                    $"Routing '{operationName}' RPC to live connection {selectedRouteIndex + 1}/{clients.Length} "
                    + $"with {activeOperationCounts[selectedRouteIndex]} active operation(s) on that connection."));
            return new RouteLease(selectedRouteIndex);
        }
    }

    private RouteLease AcquireRoute(string operationName, int routeIndex)
    {
        lock (routeLock)
        {
            activeOperationCounts[routeIndex]++;
            Report(
                PlateauLog.Debug(
                    "live",
                    $"Routing session-scoped '{operationName}' RPC to live connection {routeIndex + 1}/{clients.Length} "
                    + $"with {activeOperationCounts[routeIndex]} active operation(s) on that connection."));
            return new RouteLease(routeIndex);
        }
    }

    private void ReleaseRoute(RouteLease routeLease)
    {
        lock (routeLock)
        {
            activeOperationCounts[routeLease.RouteIndex]--;
        }
    }

    private async Task ConnectAllRoutesAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        for (int routeIndex = 0; routeIndex < clients.Length; routeIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Report(
                PlateauLog.Debug(
                    "live",
                    $"Connecting live connection {routeIndex + 1}/{clients.Length}."));
            await clients[routeIndex].ConnectAsync(endpoint, cancellationToken);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Progress reporting is diagnostic output and must not affect load-balancer route ownership or RPC results.")]
    private void Report(string message)
    {
        try
        {
            reportProgress?.Invoke(message);
        }
        catch
        {
        }
    }

    private readonly record struct RouteLease(int RouteIndex);
}
