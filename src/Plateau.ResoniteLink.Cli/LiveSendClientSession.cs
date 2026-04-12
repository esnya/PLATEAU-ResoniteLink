using System.Collections.Concurrent;

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

    private ConcurrentBag<IResoniteLinkClient>? BackgroundClients { get; set; }

    public void BeginWorkerClientTracking()
    {
        BackgroundClients ??= [];
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
                return;
            }

            IResoniteLinkClient createdClient = createConfiguredClient();
            try
            {
                await createdClient.ConnectAsync(endpoint, cancellationToken);
                SetupClient = createdClient;
                reportProgress?.Invoke(
                    $"[live] Connected setup ResoniteLink session to {endpoint} for dataset '{request.Dataset}' mesh '{request.MeshCode}'.");
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

        IResoniteLinkClient client = createConfiguredClient();
        try
        {
            using CancellationTokenSource connectCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task connectTask = client.ConnectAsync(endpoint, connectCancellation.Token);
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
            BackgroundClients.Add(client);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public Task<IResoniteLinkClient> CreateLaneClientAsync(
        PlateauImportRequest request,
        int laneIndex,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

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
            foreach (IResoniteLinkClient client in BackgroundClients)
            {
                client.Dispose();
            }
        }

        SetupClient = null;
        BackgroundClients = null;
        initializationGate.Dispose();
    }
}
