using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Logging;
namespace PlateauResoniteLink.Transport.ResoniteLink;

internal sealed class LiveSendClientSession : ILiveSendClientSession, IDisposable
{
    private readonly Func<IResoniteLinkClient> createConfiguredClient;
    private readonly Uri endpoint;
    private readonly int connectionCount;
    private readonly Action<string>? reportProgress;
    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private int disposed;
    private IResoniteLinkClient? routedClient;

    public LiveSendClientSession(
        Func<IResoniteLinkClient> createConfiguredClient,
        Uri endpoint,
        int connectionCount,
        ResoniteLinkSendDiagnostics diagnostics,
        Action<string>? progressReporter)
    {
        this.createConfiguredClient = createConfiguredClient;
        this.endpoint = endpoint;
        this.connectionCount = connectionCount;
        Diagnostics = diagnostics;
        reportProgress = progressReporter;
    }

    public ResoniteLinkSendDiagnostics Diagnostics { get; }

    private IResoniteLinkClient[]? ConnectedClients { get; set; }

    public IResoniteLinkClient GetRequiredClient()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        return routedClient
            ?? throw new InvalidOperationException("Routed ResoniteLink client is not connected.");
    }

    public async Task EnsureConnectedAsync(
        LiveSendConnectionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        await initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (routedClient is not null)
            {
                reportProgress?.Invoke(
                    PlateauLog.Info("live", "Reusing existing routed ResoniteLink session."));
                return;
            }

            if (connectionCount <= 0)
            {
                throw new InvalidOperationException("connectionCount must be greater than zero.");
            }

            IResoniteLinkClient[] newClients = new IResoniteLinkClient[connectionCount];
            List<IResoniteLinkClient> connectedClientList = [];
            try
            {
                Stopwatch setupSessionStopwatch = Stopwatch.StartNew();
                for (int routeIndex = 0; routeIndex < connectionCount; routeIndex++)
                {
                    IResoniteLinkClient client = createConfiguredClient();
                    connectedClientList.Add(client);
                    await ConnectClientAsync(
                        client,
                        request,
                        routeIndex,
                        cancellationToken);
                    newClients[routeIndex] = client;
                }

                IResoniteLinkClient newRoutedClient = new RoutedResoniteLinkClient(
                    newClients,
                    reportProgress);

                ConnectedClients = newClients;
                routedClient = newRoutedClient;

                setupSessionStopwatch.Stop();
                reportProgress?.Invoke(
                    PlateauLog.Info(
                        "live",
                        $"All {connectionCount} live-send sessions connected for dataset '{request.Dataset}' "
                        + $"and mesh '{request.MeshCode}' in {setupSessionStopwatch.Elapsed.TotalSeconds:F2}s."));
            }
            catch
            {
                foreach (IResoniteLinkClient client in connectedClientList)
                {
                    client.Dispose();
                }

                ConnectedClients = null;
                routedClient = null;
                throw;
            }
        }
        finally
        {
            try
            {
                initializationGate.Release();
            }
            catch (ObjectDisposedException)
            {
                // Session disposed while setup was in progress.
            }
        }
    }

    public async ValueTask ResetClientsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        await initializationGate.WaitAsync(cancellationToken);
        try
        {
            DisposeConnectedClients();
        }
        finally
        {
            initializationGate.Release();
        }
    }

    public void DisposeClients()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        DisposeConnectedClients();
        initializationGate.Dispose();
    }

    private void DisposeConnectedClients()
    {
        routedClient?.Dispose();
        routedClient = null;
        if (ConnectedClients is not null)
        {
            foreach (IResoniteLinkClient client in ConnectedClients)
            {
                client.Dispose();
            }
        }

        ConnectedClients = null;
    }

    private async Task ConnectClientAsync(
        IResoniteLinkClient client,
        LiveSendConnectionRequest request,
        int routeIndex,
        CancellationToken cancellationToken)
    {
        Stopwatch connectionStopwatch = Stopwatch.StartNew();
        string routeDescription = $"route {routeIndex + 1}/{connectionCount}";
        reportProgress?.Invoke(
            PlateauLog.Info(
                "live",
                $"Connecting {routeDescription} ResoniteLink session to {endpoint} for dataset '{request.Dataset}' mesh '{request.MeshCode}'."));
        await client.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
        connectionStopwatch.Stop();
        reportProgress?.Invoke(
            PlateauLog.Info(
                "live",
                $"Connected {routeDescription} ResoniteLink session to {endpoint} in "
                + $"{connectionStopwatch.Elapsed.TotalSeconds:F2}s."));
    }
}
