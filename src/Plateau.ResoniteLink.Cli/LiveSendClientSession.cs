using System.Diagnostics;

using Plateau.ResoniteLink.Application.Logging;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Cli;

internal sealed class LiveSendClientSession : ILiveSendClientSession, IDisposable
{
    private readonly Func<IResoniteLinkClient> createConfiguredClient;
    private readonly Uri endpoint;
    private readonly int connectionCount;
    private readonly int workerConnectTimeoutMilliseconds;
    private readonly Action<string>? reportProgress;
    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private readonly int workerLaneCount;
    private int disposed;

    public LiveSendClientSession(
        Func<IResoniteLinkClient> createConfiguredClient,
        Uri endpoint,
        int connectionCount,
        int workerConnectTimeoutMilliseconds,
        Action<string>? progressReporter)
    {
        this.createConfiguredClient = createConfiguredClient;
        this.endpoint = endpoint;
        this.connectionCount = connectionCount;
        this.workerConnectTimeoutMilliseconds = workerConnectTimeoutMilliseconds;
        reportProgress = progressReporter;
        workerLaneCount = Math.Max(connectionCount - 1, 0);
    }

    public IResoniteLinkClient? SetupClient { get; private set; }

    private IResoniteLinkClient[]? workerClients;

    public void BeginWorkerClientTracking()
    {
        reportProgress?.Invoke(
            PlateauLog.Info("live", "Worker lanes are connected eagerly during setup; BeginWorkerClientTracking is non-blocking."));
    }

    public async Task EnsureSetupClientConnectedAsync(
        PlateauImportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        await initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (SetupClient is not null)
            {
                reportProgress?.Invoke(
                    PlateauLog.Info("live", "Reusing existing setup ResoniteLink session."));
                return;
            }

            IResoniteLinkClient setupClient = createConfiguredClient();
            IResoniteLinkClient[] newWorkerClients = new IResoniteLinkClient[workerLaneCount];
            List<IResoniteLinkClient> connectedClients = [];
            try
            {
                Stopwatch setupSessionStopwatch = Stopwatch.StartNew();
                reportProgress?.Invoke(
                    PlateauLog.Info(
                        "live",
                        $"Connecting setup ResoniteLink session to {endpoint} for dataset '{request.Dataset}' mesh '{request.MeshCode}'."));
                await ConnectClientAsync(setupClient, request, laneIndex: 0, cancellationToken);
                connectedClients.Add(setupClient);
                setupSessionStopwatch.Stop();
                reportProgress?.Invoke(
                    PlateauLog.Info(
                        "live",
                        $"Setup ResoniteLink session connected to {endpoint} in {setupSessionStopwatch.Elapsed.TotalSeconds:F2}s "
                        + $"for dataset '{request.Dataset}' mesh '{request.MeshCode}'."));

                for (int laneIndex = 1; laneIndex < connectionCount; laneIndex++)
                {
                    IResoniteLinkClient workerClient = createConfiguredClient();
                    connectedClients.Add(workerClient);
                    await ConnectClientAsync(
                        workerClient,
                        request,
                        laneIndex,
                        cancellationToken);
                    newWorkerClients[laneIndex - 1] = workerClient;
                }

                SetupClient = setupClient;
                workerClients = newWorkerClients;
                reportProgress?.Invoke(
                    PlateauLog.Info(
                        "live",
                        $"All {connectionCount} live-send sessions connected (setup=1, workers={workerLaneCount}) for dataset '{request.Dataset}'."));
            }
            catch
            {
                foreach (IResoniteLinkClient client in connectedClients)
                {
                    client.Dispose();
                }

                SetupClient = null;
                workerClients = null;
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

    public async Task<IResoniteLinkClient> CreateWorkerClientAsync(
        PlateauImportRequest request,
        int laneIndex,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        ThrowIfLaneIndexOutsideCapacity(laneIndex);
        if (laneIndex == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(laneIndex), laneIndex, "Worker lanes start at index 1.");
        }

        if (workerClients is null)
        {
            throw new InvalidOperationException("Worker sessions are not connected. Call EnsureSetupClientConnectedAsync before creating worker clients.");
        }

        return await Task.FromResult(workerClients[laneIndex - 1]);
    }

    public Task<IResoniteLinkClient> CreateLaneClientAsync(
        PlateauImportRequest request,
        int laneIndex,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfLaneIndexOutsideCapacity(laneIndex);

        return laneIndex == 0
            ? Task.FromResult(SetupClient ?? throw new InvalidOperationException("Setup client is not connected."))
            : CreateWorkerClientAsync(request, laneIndex, cancellationToken);
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

        SetupClient?.Dispose();
        if (workerClients is not null)
        {
            foreach (IResoniteLinkClient client in workerClients)
            {
                client.Dispose();
            }
        }

        SetupClient = null;
        workerClients = null;
        initializationGate.Dispose();
    }

    private void ThrowIfLaneIndexOutsideCapacity(int laneIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(laneIndex);
        if (laneIndex >= connectionCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(laneIndex),
                laneIndex,
                $"Lane index must be smaller than the configured active lane capacity ({connectionCount}).");
        }
    }

    private async Task ConnectClientAsync(
        IResoniteLinkClient client,
        PlateauImportRequest request,
        int laneIndex,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource connectCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task connectTask = client.ConnectAsync(endpoint, connectCancellation.Token);
        Stopwatch connectionStopwatch = Stopwatch.StartNew();
        string laneDescription = laneIndex == 0
            ? "setup"
            : $"worker {laneIndex}/{connectionCount}";
        reportProgress?.Invoke(
            PlateauLog.Info(
                "live",
                $"Connecting {laneDescription} ResoniteLink session to {endpoint} for dataset '{request.Dataset}' mesh '{request.MeshCode}'."));
        if (!connectTask.IsCompleted)
        {
            Task completedTask = await Task.WhenAny(
                connectTask,
                Task.Delay(workerConnectTimeoutMilliseconds, connectCancellation.Token));
            if (completedTask != connectTask)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                _ = connectCancellation.CancelAsync();
                _ = connectTask.ContinueWith(
                    static completedConnectTask => _ = completedConnectTask.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                throw new TimeoutException(
                    $"ResoniteLink {(laneIndex == 0 ? "setup" : $"worker {laneIndex}/{connectionCount}")} session "
                    + $"did not connect within {workerConnectTimeoutMilliseconds}ms.");
            }
        }

        await connectTask.ConfigureAwait(false);
        connectionStopwatch.Stop();
        reportProgress?.Invoke(
            PlateauLog.Info(
                "live",
                $"Connected {laneDescription} ResoniteLink session to {endpoint} in "
                + $"{connectionStopwatch.Elapsed.TotalSeconds:F2}s."));
    }
}
