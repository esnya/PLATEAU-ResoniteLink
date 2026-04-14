using System.Collections.Concurrent;
using System.Diagnostics;

using Plateau.ResoniteLink.Domain.Importing;
using Plateau.ResoniteLink.Application.Logging;

using ResoniteLink;

namespace Plateau.ResoniteLink.Cli;

internal sealed class LiveSendClientSession : ILiveSendClientSession, IDisposable
{
    private readonly Func<IResoniteLinkClient> createConfiguredClient;
    private readonly Uri endpoint;
    private readonly int connectionCount;
    private readonly int workerConnectTimeoutMilliseconds;
    private readonly Action<string>? reportProgress;
    private readonly SemaphoreSlim initializationGate = new(1, 1);
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
    }

    public IResoniteLinkClient? SetupClient { get; private set; }

    private ConcurrentDictionary<int, IResoniteLinkClient>? BackgroundClients { get; set; }

    public void BeginWorkerClientTracking()
    {
        BackgroundClients ??= [];
        reportProgress?.Invoke(
            PlateauLog.Info(
                "live",
                $"Worker session tracker initialized for {Math.Max(connectionCount - 1, 0)} background lane capacity."));
    }

    public async Task EnsureSetupClientConnectedAsync(
        PlateauImportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (SetupClient is not null)
            {
                reportProgress?.Invoke(
                    PlateauLog.Info("live", "Reusing existing setup ResoniteLink session."));
                return;
            }

            Stopwatch setupSessionStopwatch = Stopwatch.StartNew();
            IResoniteLinkClient createdClient = createConfiguredClient();
            try
            {
                reportProgress?.Invoke(
                    PlateauLog.Info(
                        "live",
                        $"Connecting setup ResoniteLink session to {endpoint} for dataset '{request.Dataset}' mesh '{request.MeshCode}'."));
                await createdClient.ConnectAsync(endpoint, cancellationToken);
                setupSessionStopwatch.Stop();
                SetupClient = createdClient;
                reportProgress?.Invoke(
                    PlateauLog.Info(
                        "live",
                        $"Setup ResoniteLink session connected to {endpoint} in {setupSessionStopwatch.Elapsed.TotalSeconds:F2}s "
                        + $"for dataset '{request.Dataset}' mesh '{request.MeshCode}'."));
            }
            catch
            {
                createdClient.Dispose();
                throw;
            }
        }
        finally
        {
            initializationGate.Release();
        }
    }

    public async Task<IResoniteLinkClient> CreateWorkerClientAsync(
        PlateauImportRequest request,
        int laneIndex,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(BackgroundClients is null, this);
        ThrowIfLaneIndexOutsideCapacity(laneIndex);
        if (laneIndex == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(laneIndex), laneIndex, "Worker lanes start at index 1.");
        }

        IResoniteLinkClient client = BackgroundClients.GetOrAdd(
            laneIndex,
            static (index, state) => new LazyWorkerClient(
                state.CreateConfiguredClient,
                state.Endpoint,
                state.ConnectionCount,
                state.WorkerConnectTimeoutMilliseconds,
                index,
                state.Request,
                state.ProgressReporter),
            (
                CreateConfiguredClient: createConfiguredClient,
                Endpoint: endpoint,
                ConnectionCount: connectionCount,
                WorkerConnectTimeoutMilliseconds: workerConnectTimeoutMilliseconds,
                Request: request,
                ProgressReporter: reportProgress));
        return await Task.FromResult(client);
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

        if (BackgroundClients is not null)
        {
            foreach (IResoniteLinkClient client in BackgroundClients.Values)
            {
                client.Dispose();
            }
        }

        SetupClient = null;
        BackgroundClients = null;
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

    private sealed class LazyWorkerClient(
        Func<IResoniteLinkClient> createConfiguredClient,
        Uri endpoint,
        int connectionCount,
        int workerConnectTimeoutMilliseconds,
        int laneIndex,
        PlateauImportRequest request,
        Action<string>? progressReporter) : IResoniteLinkClient
    {
        private readonly SemaphoreSlim connectGate = new(1, 1);
        private IResoniteLinkClient? inner;
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            inner?.Dispose();
            connectGate.Dispose();
        }

        public Task ConnectAsync(Uri _, CancellationToken cancellationToken)
        {
            return EnsureConnectedAsync(cancellationToken);
        }

        public async Task<string> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
        {
            IResoniteLinkClient client = await GetClientAsync(cancellationToken);
            return await client.AddComponentAsync(request, cancellationToken);
        }

        public async Task<string> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
        {
            IResoniteLinkClient client = await GetClientAsync(cancellationToken);
            return await client.AddSlotAsync(request, cancellationToken);
        }

        public async Task<BatchResponse> RunDataModelOperationBatchAsync(
            IReadOnlyList<DataModelOperation> operations,
            CancellationToken cancellationToken)
        {
            IResoniteLinkClient client = await GetClientAsync(cancellationToken);
            return await client.RunDataModelOperationBatchAsync(operations, cancellationToken);
        }

        public async Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
        {
            IResoniteLinkClient client = await GetClientAsync(cancellationToken);
            return await client.GetComponentAsync(componentId, cancellationToken);
        }

        public async Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
        {
            IResoniteLinkClient client = await GetClientAsync(cancellationToken);
            return await client.GetSlotAsync(slotId, depth, cancellationToken);
        }

        public async Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
        {
            IResoniteLinkClient client = await GetClientAsync(cancellationToken);
            return await client.ImportMeshAsync(request, cancellationToken);
        }

        public async Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
        {
            IResoniteLinkClient client = await GetClientAsync(cancellationToken);
            return await client.ImportTextureAsync(textureImport, cancellationToken);
        }

        public async Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
        {
            IResoniteLinkClient client = await GetClientAsync(cancellationToken);
            await client.UpdateComponentAsync(request, cancellationToken);
        }

        private async Task<IResoniteLinkClient> GetClientAsync(CancellationToken cancellationToken)
        {
            await EnsureConnectedAsync(cancellationToken);
            return inner ?? throw new InvalidOperationException("Worker client did not finish connecting.");
        }

        private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            if (inner is not null)
            {
                return;
            }

            await connectGate.WaitAsync(cancellationToken);
            try
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
                if (inner is not null)
                {
                    return;
                }

                Stopwatch workerSessionStopwatch = Stopwatch.StartNew();
                progressReporter?.Invoke(
                    PlateauLog.Info(
                        "live",
                        $"Connecting worker ResoniteLink session {laneIndex + 1}/{connectionCount} to {endpoint} "
                        + $"for dataset '{request.Dataset}' mesh '{request.MeshCode}'."));

                IResoniteLinkClient createdClient = createConfiguredClient();
                try
                {
                    using CancellationTokenSource connectCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    Task connectTask = createdClient.ConnectAsync(endpoint, connectCancellation.Token);
                    if (!connectTask.IsCompleted)
                    {
                        Task completedTask = await Task.WhenAny(
                            connectTask,
                            Task.Delay(workerConnectTimeoutMilliseconds, cancellationToken));
                        if (completedTask != connectTask)
                        {
                            await connectCancellation.CancelAsync();
                            _ = connectTask.ContinueWith(
                                static completedConnectTask => _ = completedConnectTask.Exception,
                                CancellationToken.None,
                                TaskContinuationOptions.ExecuteSynchronously,
                                TaskScheduler.Default);
                            cancellationToken.ThrowIfCancellationRequested();
                            throw new TimeoutException(
                                $"ResoniteLink worker session {laneIndex + 1}/{connectionCount} did not connect within {workerConnectTimeoutMilliseconds}ms.");
                        }
                    }

                    await connectTask;
                    inner = createdClient;
                    createdClient = null!;
                    workerSessionStopwatch.Stop();
                    progressReporter?.Invoke(
                        PlateauLog.Info(
                            "live",
                            $"Connected worker ResoniteLink session {laneIndex + 1}/{connectionCount} to {endpoint} in "
                            + $"{workerSessionStopwatch.Elapsed.TotalSeconds:F2}s."));
                }
                catch
                {
                    createdClient.Dispose();
                    throw;
                }
            }
            finally
            {
                connectGate.Release();
            }
        }
    }
}
