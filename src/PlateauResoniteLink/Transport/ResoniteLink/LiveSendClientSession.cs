using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using PlateauResoniteLink.Diagnostics;

namespace PlateauResoniteLink.Transport.ResoniteLink;

internal sealed class LiveSendClientSession : ILiveSendClientSession, IDisposable
{
    private readonly Func<IResoniteLinkClient> createConfiguredClient;
    private readonly Uri endpoint;
    private readonly int connectionCount;
    private readonly ILogger logger;
    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private int disposed;
    private IResoniteLinkClient? loadBalancedClient;

    public LiveSendClientSession(
        Func<IResoniteLinkClient> createConfiguredClient,
        Uri endpoint,
        int connectionCount,
        ResoniteLinkSendDiagnostics diagnostics,
        ILogger logger)
    {
        this.createConfiguredClient = createConfiguredClient;
        this.endpoint = endpoint;
        this.connectionCount = connectionCount;
        Diagnostics = diagnostics;
        this.logger = logger;
    }

    public ResoniteLinkSendDiagnostics Diagnostics { get; }

    private IResoniteLinkClient[]? ConnectedClients { get; set; }

    public IResoniteLinkClient GetRequiredClient()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        return loadBalancedClient
            ?? throw new InvalidOperationException("Load-balanced ResoniteLink client is not connected.");
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
            if (loadBalancedClient is not null)
            {
                logger.WriteInformation("Reusing existing load-balanced ResoniteLink session.");
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

                IResoniteLinkClient newLoadBalancedClient = new LoadBalancingResoniteLinkClient(
                    newClients,
                    logger);

                ConnectedClients = newClients;
                loadBalancedClient = newLoadBalancedClient;

                setupSessionStopwatch.Stop();
                logger.WriteInformation(
                    "All {ConnectionCount} live-send sessions connected for dataset '{Dataset}' and mesh '{MeshCode}' in {ElapsedSeconds:F2}s.",
                    connectionCount,
                    request.Dataset,
                    request.MeshCode,
                    setupSessionStopwatch.Elapsed.TotalSeconds);
            }
            catch
            {
                foreach (IResoniteLinkClient client in connectedClientList)
                {
                    client.Dispose();
                }

                ConnectedClients = null;
                loadBalancedClient = null;
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
        loadBalancedClient?.Dispose();
        loadBalancedClient = null;
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
        int connectionIndex,
        CancellationToken cancellationToken)
    {
        Stopwatch connectionStopwatch = Stopwatch.StartNew();
        string connectionDescription = $"connection {connectionIndex + 1}/{connectionCount}";
        logger.WriteInformation(
            "Connecting {ConnectionDescription} ResoniteLink session to {Endpoint} for dataset '{Dataset}' mesh '{MeshCode}'.",
            connectionDescription,
            endpoint,
            request.Dataset,
            request.MeshCode);
        await client.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
        connectionStopwatch.Stop();
        logger.WriteInformation(
            "Connected {ConnectionDescription} ResoniteLink session to {Endpoint} in {ElapsedSeconds:F2}s.",
            connectionDescription,
            endpoint,
            connectionStopwatch.Elapsed.TotalSeconds);
    }
}
