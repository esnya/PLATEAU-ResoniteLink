using System.Diagnostics;

using Plateau.ResoniteLink.Application.Logging;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Transport.ResoniteLink;

internal sealed class LiveSendClientSession : ILiveSendClientSession, IDisposable
{
    private readonly Func<IResoniteLinkClient> createConfiguredClient;
    private readonly Uri endpoint;
    private readonly int connectionCount;
    private readonly Action<string>? reportProgress;
    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private int disposed;

    public LiveSendClientSession(
        Func<IResoniteLinkClient> createConfiguredClient,
        Uri endpoint,
        int connectionCount,
        Action<string>? progressReporter)
    {
        this.createConfiguredClient = createConfiguredClient;
        this.endpoint = endpoint;
        this.connectionCount = connectionCount;
        reportProgress = progressReporter;
    }

    public IResoniteLinkClient? RoutedClient { get; private set; }

    private IResoniteLinkClient[]? ConnectedClients { get; set; }

    public void BeginWorkerClientTracking()
    {
        reportProgress?.Invoke(
            PlateauLog.Info(
                "live",
                "Live-send routes are connected eagerly during setup; BeginWorkerClientTracking is non-blocking."));
    }

    public async Task EnsureConnectedAsync(
        PlateauImportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        await initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (RoutedClient is not null)
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
                RoutedClient = newRoutedClient;

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
                RoutedClient = null;
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

    public Task EnsureSetupClientConnectedAsync(
        PlateauImportRequest request,
        CancellationToken cancellationToken)
    {
        return EnsureConnectedAsync(request, cancellationToken);
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

        RoutedClient?.Dispose();
        RoutedClient = null;
        if (ConnectedClients is not null)
        {
            foreach (IResoniteLinkClient client in ConnectedClients)
            {
                client.Dispose();
            }
        }

        ConnectedClients = null;
        initializationGate.Dispose();
    }

    private async Task ConnectClientAsync(
        IResoniteLinkClient client,
        PlateauImportRequest request,
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
