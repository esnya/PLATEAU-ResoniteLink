using PlateauResoniteLink.Application.Logging;

using ResoniteLink;

namespace PlateauResoniteLink.Transport.ResoniteLink;

internal sealed class RoutedResoniteLinkClient : IResoniteLinkClient
{
    private readonly IResoniteLinkClient[] routedClients;
    private readonly Action<string>? reportProgress;
    private readonly int routeCount;
    private int routeCursor;
    private int disposed;

    public RoutedResoniteLinkClient(IReadOnlyList<IResoniteLinkClient> clients, Action<string>? reportProgress = null)
    {
        routedClients = clients is null ? throw new ArgumentNullException(nameof(clients)) : [.. clients];
        this.reportProgress = reportProgress;
        if (routedClients.Length == 0)
        {
            throw new ArgumentException("At least one client must be configured for routing.", nameof(clients));
        }

        routeCount = routedClients.Length;
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

    public Task<string> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
    {
        return RouteForAuthoritative("add_component").AddComponentAsync(request, cancellationToken);
    }

    public Task<string> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
    {
        return RouteForAuthoritative("add_slot").AddSlotAsync(request, cancellationToken);
    }

    public Task<BatchResponse> RunDataModelOperationBatchAsync(
        IReadOnlyList<DataModelOperation> operations,
        CancellationToken cancellationToken)
    {
        return RouteForBalanced("run_data_model_operation_batch").RunDataModelOperationBatchAsync(
            operations,
            cancellationToken);
    }

    public Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
    {
        return RouteForAuthoritative("get_slot").GetSlotAsync(slotId, depth, cancellationToken);
    }

    public Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
    {
        return RouteForAuthoritative("get_component").GetComponentAsync(componentId, cancellationToken);
    }

    public Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
    {
        return RouteForBalanced("import_mesh").ImportMeshAsync(request, cancellationToken);
    }

    public Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
    {
        return RouteForBalanced("import_texture").ImportTextureAsync(textureImport, cancellationToken);
    }

    public Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
    {
        return RouteForAuthoritative("update_component").UpdateComponentAsync(request, cancellationToken);
    }

    private IResoniteLinkClient RouteForAuthoritative(string operationName)
    {
        ReportRoute(operationName, 0);
        return routedClients[0];
    }

    private IResoniteLinkClient RouteForBalanced(string operationName)
    {
        if (routeCount == 1)
        {
            ReportRoute(operationName, 0);
            return routedClients[0];
        }

        int routeIndex = (int)((uint)Interlocked.Increment(ref routeCursor) % (uint)routeCount);
        ReportRoute(operationName, routeIndex);
        return routedClients[routeIndex];
    }

    private void ReportRoute(string operationName, int routeIndex)
    {
        reportProgress?.Invoke(
            PlateauLog.Debug(
                "live",
                $"Routing '{operationName}' RPC to connection route {routeIndex + 1}/{routeCount}."));
    }

    private async Task ConnectAllRoutesAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        for (int routeIndex = 0; routeIndex < routeCount; routeIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReportRoute("connect", routeIndex);
            await routedClients[routeIndex].ConnectAsync(endpoint, cancellationToken);
        }
    }
}
