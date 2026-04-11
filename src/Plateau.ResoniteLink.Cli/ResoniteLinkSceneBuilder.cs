using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;

using GeographicLib;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Application.Logging;
using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Cli;

public sealed class ResoniteLinkSceneBuilder : IResoniteSceneBuilder
{
    private const int MaxQueuedCityObjects = 4;
    private const int VisibilityPollDelayMilliseconds = 50;
    private const int VisibilityPollAttemptLimit = 200;
    private const int ExistingDatasetRootPollAttemptLimit = 30;
    private const int WorkerConnectTimeoutMilliseconds = 5000;
    private const string RootSlotId = "Root";
    private const string CommonAssetsSlotName = "Common";
    private const string DemPackageName = "dem";
    private const string HeightMapAssetSlotSuffix = "_heightmap";
    private readonly Func<IResoniteLinkClient> clientFactory;
    private readonly Uri endpoint;
    private readonly int connectionCount;
    private readonly ResoniteLinkSendDiagnostics diagnostics;
    private readonly ITerrainTextureAssetGenerator terrainTextureAssetGenerator;
    private readonly ResoniteGeometryAssetAssembler geometryAssetAssembler;
    private readonly SemaphoreSlim clientInitializationGate = new(1, 1);
    private readonly Action<string>? progressReporter;
    private readonly AsyncCompletedResultCache<(string ParentSlotId, string SlotName), CreatedSlot> sharedSlotCache = new();
    private IResoniteLinkClient? setupClient;
    private ConcurrentBag<IResoniteLinkClient>? backgroundClients;
    private ResoniteConstructionMetadata? metadata;
    private CreatedSlot? datasetRootSlot;
    private CreatedSlot? datasetAssetsRootSlot;
    private CreatedSlot? commonAssetsRootSlot;
    private ResoniteMaterialAssetManager? materialAssetManager;
    private string? generatedAssetsRoot;
    private AsyncCompletedResultCache<TextureImportCacheKey, Uri>? importedTextureUriCache;
    private DispatchLaneAllocator? dispatchLaneAllocator;
    private ResoniteTextureImportResolver? textureImportResolver;
    private Channel<QueuedCityObject>[]? cityObjectChannels;
    private Task[]? processingTasks;
    private CancellationTokenSource? processingCancellationSource;
    private TaskCompletionSource<Exception>? firstProcessingFailureSource;
    private int processedCityObjectCount;
    private Stopwatch? sceneBuildStopwatch;
    private int firstQueuedCityObjectLogged;
    private int firstPreparedCityObjectLogged;
    private int firstBuiltCityObjectLogged;
    private IPlateauDatasetContentSource? datasetContentSource;
    private SceneAnchor? sceneAnchor;

    public ResoniteLinkSceneBuilder(Uri endpoint, Action<string>? progressReporter = null)
        : this(endpoint, 4, ResoniteLinkSendDiagnostics.Disabled, static () => new ResoniteLinkClient(), new TerrainTextureAssetGenerator(), progressReporter)
    {
    }

    public ResoniteLinkSceneBuilder(Uri endpoint, int connectionCount, Action<string>? progressReporter = null)
        : this(endpoint, connectionCount, ResoniteLinkSendDiagnostics.Disabled, static () => new ResoniteLinkClient(), new TerrainTextureAssetGenerator(), progressReporter)
    {
    }

    internal ResoniteLinkSceneBuilder(
        Uri endpoint,
        int connectionCount,
        ResoniteLinkSendDiagnostics diagnostics,
        Action<string>? progressReporter = null)
        : this(endpoint, connectionCount, diagnostics, static () => new ResoniteLinkClient(), new TerrainTextureAssetGenerator(), progressReporter)
    {
    }

    internal ResoniteLinkSceneBuilder(
        Uri endpoint,
        int connectionCount,
        ResoniteLinkSendDiagnostics diagnostics,
        Func<IResoniteLinkClient> clientFactory,
        ITerrainTextureAssetGenerator? terrainTextureAssetGenerator = null,
        Action<string>? progressReporter = null)
    {
        this.endpoint = endpoint;
        this.connectionCount = connectionCount;
        this.diagnostics = diagnostics;
        this.clientFactory = clientFactory;
        this.terrainTextureAssetGenerator = terrainTextureAssetGenerator ?? new TerrainTextureAssetGenerator();
        this.progressReporter = progressReporter;
        geometryAssetAssembler = new ResoniteGeometryAssetAssembler(
            CreateSlotAsync,
            CreateComponentAsync,
            ReportProgress);
    }

    public async Task EnsureConnectedAsync(
        PlateauImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await EnsureSetupClientConnectedAsync(request, cancellationToken);
    }

    public async Task BeginAsync(
        ResoniteConstructionMetadata metadata,
        string workRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);

        if (this.metadata is not null || processingTasks is not null)
        {
            throw new InvalidOperationException("A live scene build run is already active on this scene builder instance.");
        }

        this.metadata = metadata;
        string resolvedWorkRoot = Path.GetFullPath(workRoot);
        Directory.CreateDirectory(resolvedWorkRoot);
        generatedAssetsRoot = Path.Combine(resolvedWorkRoot, ".generated-assets");
        string completionMeshCode = ResolveCompletionMeshCode(metadata);

        ReportProgress(
            $"[live] Initializing scene state for dataset '{metadata.Request.Dataset}' "
            + $"mesh '{metadata.Request.MeshCode}' at '{resolvedWorkRoot}'.");
        ReportProgress(
            $"[live] Connecting setup ResoniteLink session to {endpoint} "
            + $"and scheduling {Math.Max(connectionCount - 1, 0)} worker session(s).");
        await EnsureSetupClientConnectedAsync(metadata.Request, cancellationToken);
        ObjectDisposedException.ThrowIf(setupClient is null, this);
        importedTextureUriCache = new();
        dispatchLaneAllocator = new DispatchLaneAllocator(connectionCount);
        materialAssetManager = new ResoniteMaterialAssetManager(
            CreateSharedAssetComponentAsync,
            CreateDedicatedAssetComponentAsync,
            (client, parentSlotId, slotName, ct) =>
                GetOrCreateSharedChildSlotByIdAsync(client, parentSlotId, slotName, null, null, ct),
            CreateComponentAsync,
            static (client, slotId, depth, ct) => client.GetSlotAsync(slotId, depth, ct),
            ImportTextureAsync,
            ReportProgress);
        PlateauLocalImportSource localSource = metadata.Request.Source as PlateauLocalImportSource
            ?? throw new InvalidOperationException("Live scene building requires a resolved local dataset source.");

        ReportProgress("[live] Opening resolved dataset content source for texture materialization.");
        datasetContentSource = await PlateauDatasetContentSourceFactory.CreateAsync(localSource.LocalSourcePath!, cancellationToken);
        textureImportResolver = new ResoniteTextureImportResolver(
            datasetContentSource,
            generatedAssetsRoot,
            metadata.SourceDataset.TerrainTextureOverlays,
            terrainTextureAssetGenerator);
        ReportProgress("[live] Creating dataset root, asset groups, and anchor slots.");
        (datasetRootSlot, datasetAssetsRootSlot, commonAssetsRootSlot, bool datasetRootExisted) =
            await CreateSetupSlotHierarchyAsync(setupClient, cancellationToken);
        await CreateComponentAsync(
            setupClient,
            datasetRootSlot.Value.SlotId,
            "[FrooxEngine]FrooxEngine.License",
            CreateDatasetLicenseMembers(metadata.Attribution.DatasetLicense),
            cancellationToken);
        sceneAnchor = await ResolveSceneAnchorAsync(
            setupClient,
            datasetRootSlot.Value,
            completionMeshCode,
            datasetRootExisted,
            cancellationToken);

        ReportProgress("[live] Dataset slots and asset groups are ready.");
        backgroundClients = [];
        cityObjectChannels = Enumerable.Range(0, connectionCount)
            .Select(_ => Channel.CreateBounded<QueuedCityObject>(
                new BoundedChannelOptions(Math.Max(MaxQueuedCityObjects, connectionCount))
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait,
                }))
            .ToArray();
        processedCityObjectCount = 0;
        sceneBuildStopwatch = Stopwatch.StartNew();
        firstQueuedCityObjectLogged = 0;
        firstPreparedCityObjectLogged = 0;
        firstBuiltCityObjectLogged = 0;
        diagnostics.StartSendWindow(connectionCount);
        processingCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        firstProcessingFailureSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        processingTasks = CreateProcessingTasks(metadata.Request, processingCancellationSource.Token);
        ReportProgress($"[live] Send lanes ready (setup=1, workers={Math.Max(connectionCount - 1, 0)}).");
    }

    private async Task<(CreatedSlot DatasetRoot, CreatedSlot DatasetAssetsRoot, CreatedSlot CommonAssetsRoot, bool DatasetRootExisted)> CreateSetupSlotHierarchyAsync(
        IResoniteLinkClient client,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(metadata is null, this);

        (CreatedSlot datasetRoot, bool datasetRootExisted) = await GetOrCreateDatasetRootAsync(
            client,
            $"PLATEAU {metadata.Request.Dataset}",
            cancellationToken);
        CreatedSlot datasetAssetsRoot = await GetOrCreateSharedChildSlotAsync(
            client,
            datasetRoot,
            "Assets",
            null,
            null,
            cancellationToken);
        CreatedSlot commonAssetsRoot = await GetOrCreateSharedChildSlotAsync(
            client,
            datasetAssetsRoot,
            CommonAssetsSlotName,
            null,
            null,
            cancellationToken);
        return (datasetRoot, datasetAssetsRoot, commonAssetsRoot, datasetRootExisted);
    }

    private static async Task<(CreatedSlot Slot, bool Existed)> GetOrCreateDatasetRootAsync(
        IResoniteLinkClient client,
        string slotName,
        CancellationToken cancellationToken)
    {
        await WaitForSlotAvailableAsync(client, RootSlotId, cancellationToken);
        CreatedSlot? existingDatasetRoot = await TryGetUniqueChildSlotByNameWithPollingAsync(
            client,
            RootSlotId,
            slotName,
            ExistingDatasetRootPollAttemptLimit,
            cancellationToken);
        if (existingDatasetRoot is not null)
        {
            return (existingDatasetRoot.Value, true);
        }

        CreatedSlot createdDatasetRoot = await CreateSlotCoreAsync(
            client,
            RootSlotId,
            slotName,
            new ResoniteFloat3(0.0, 0.0, 0.0),
            null,
            cancellationToken);
        return (createdDatasetRoot, false);
    }

    private async Task EnsureSetupClientConnectedAsync(
        PlateauImportRequest request,
        CancellationToken cancellationToken)
    {
        await clientInitializationGate.WaitAsync(cancellationToken);
        try
        {
            if (setupClient is not null)
            {
                return;
            }

            IResoniteLinkClient createdClient = CreateConfiguredClient();
            try
            {
                await createdClient.ConnectAsync(endpoint, cancellationToken);
                setupClient = createdClient;
                ReportProgress(
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
            clientInitializationGate.Release();
        }
    }

    private IResoniteLinkClient CreateConfiguredClient()
    {
        IResoniteLinkClient client = new RetryingResoniteLinkClient(
            clientFactory,
            ReportProgress);
        return diagnostics.Enabled ? new MetricsResoniteLinkClient(client, diagnostics) : client;
    }

    private Task[] CreateProcessingTasks(
        PlateauImportRequest request,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(setupClient is null, this);
        ObjectDisposedException.ThrowIf(cityObjectChannels is null, this);
        ObjectDisposedException.ThrowIf(backgroundClients is null, this);

        Task[] tasks = new Task[connectionCount];
        tasks[0] = ProcessQueuedCityObjectsAsync(cityObjectChannels[0].Reader, setupClient, laneIndex: 0, cancellationToken);

        for (int laneIndex = 1; laneIndex < connectionCount; laneIndex++)
        {
            int capturedLaneIndex = laneIndex;
            tasks[capturedLaneIndex] = ConnectWorkerAndProcessQueuedCityObjectsAsync(
                cityObjectChannels[capturedLaneIndex].Reader,
                request,
                capturedLaneIndex,
                cancellationToken);
        }

        return tasks;
    }

    private async Task ProcessQueuedCityObjectsAsync(
        ChannelReader<QueuedCityObject> reader,
        IResoniteLinkClient client,
        int laneIndex,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (QueuedCityObject queuedCityObject in reader.ReadAllAsync(cancellationToken))
            {
                await ProcessQueuedCityObjectAsync(client, queuedCityObject, cancellationToken);
            }

            ReportProgress($"[live] Send lane {laneIndex + 1}/{connectionCount} drained.");
        }
        catch (OperationCanceledException)
        {
            ReportProgress($"[live][warn] Send lane {laneIndex + 1}/{connectionCount} canceled.");
            throw;
        }
        catch (Exception exception)
        {
            TryMarkProcessingFailure(exception);
            CancelProcessing();
            ReportProgress($"[live][error] Send lane {laneIndex + 1}/{connectionCount} failed: {exception.Message}");
            throw;
        }
    }

    private async Task ConnectWorkerAndProcessQueuedCityObjectsAsync(
        ChannelReader<QueuedCityObject> reader,
        PlateauImportRequest request,
        int laneIndex,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(backgroundClients is null, this);

        IResoniteLinkClient client = CreateConfiguredClient();
        bool addedToBackgroundClients = false;
        try
        {
            await ConnectWorkerClientAsync(client, laneIndex, cancellationToken);
            backgroundClients.Add(client);
            addedToBackgroundClients = true;
            ReportProgress(
                $"[live] Connected worker ResoniteLink session {laneIndex + 1}/{connectionCount} "
                + $"to {endpoint} for dataset '{request.Dataset}' mesh '{request.MeshCode}'.");
            await ProcessQueuedCityObjectsAsync(reader, client, laneIndex, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!addedToBackgroundClients)
            {
                client.Dispose();
            }

            throw;
        }
        catch (Exception exception)
        {
            TryMarkProcessingFailure(exception);
            CancelProcessing();
            if (!addedToBackgroundClients)
            {
                client.Dispose();
            }

            throw;
        }
    }

    private async Task ConnectWorkerClientAsync(
        IResoniteLinkClient client,
        int laneIndex,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource connectCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task connectTask = client.ConnectAsync(endpoint, connectCancellation.Token);
        if (connectTask.IsCompleted)
        {
            await connectTask;
            return;
        }

        Task completedTask = await Task.WhenAny(
            connectTask,
            Task.Delay(WorkerConnectTimeoutMilliseconds, cancellationToken));
        if (completedTask == connectTask)
        {
            await connectTask;
            return;
        }

        await connectCancellation.CancelAsync();
        _ = connectTask.ContinueWith(
            static completedConnectTask => _ = completedConnectTask.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException(
            $"ResoniteLink worker session {laneIndex + 1}/{connectionCount} did not connect within {WorkerConnectTimeoutMilliseconds}ms.");
    }

    public async Task ProcessCityObjectAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        ObjectDisposedException.ThrowIf(metadata is null, this);
        ObjectDisposedException.ThrowIf(cityObjectChannels is null, this);
        ObjectDisposedException.ThrowIf(processingTasks is null, this);

        await AwaitProcessingTasksIfCompletedAsync();

        Task<PreparedCityObject> preparationTask = CreatePreparationTask(cityObject, cancellationToken);
        if (Interlocked.CompareExchange(ref firstQueuedCityObjectLogged, 1, 0) == 0)
        {
            ReportProgress(
                $"[live] First city object queued after {GetSceneElapsedSeconds():F3}s: "
                + $"{cityObject.DisplayName} ({cityObject.PackageName}/{cityObject.SlotKey})");
        }

        ObjectDisposedException.ThrowIf(dispatchLaneAllocator is null, this);

        int dispatchLane = dispatchLaneAllocator.GetLane(cityObject);
        using CancellationTokenSource enqueueCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            processingCancellationSource?.Token ?? CancellationToken.None);
        try
        {
            await cityObjectChannels[dispatchLane].Writer.WriteAsync(
                new QueuedCityObject(cityObject, preparationTask),
                enqueueCancellation.Token);
        }
        catch (OperationCanceledException) when (processingCancellationSource?.IsCancellationRequested == true)
        {
            await AwaitProcessingTasksIfCompletedAsync();
            throw;
        }

        await AwaitProcessingTasksIfCompletedAsync();
    }

    public async Task<IReadOnlyList<string>> CompleteAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(cityObjectChannels is null, this);
        ObjectDisposedException.ThrowIf(processingTasks is null, this);

        ReportProgress("[live] Completing live send. Closing lane writers.");
        foreach (Channel<QueuedCityObject> channel in cityObjectChannels)
        {
            channel.Writer.TryComplete();
        }

        ReportProgress($"[live] Awaiting {processingTasks.Length} send lane task(s) to drain.");
        Task allProcessingTasks = Task.WhenAll(processingTasks);
        if (firstProcessingFailureSource is not null)
        {
            Task completedTask = await Task.WhenAny(allProcessingTasks, firstProcessingFailureSource.Task).WaitAsync(cancellationToken);
            if (completedTask == firstProcessingFailureSource.Task)
            {
                CancelProcessing();
                Exception failure = await firstProcessingFailureSource.Task.WaitAsync(cancellationToken);
                throw failure;
            }
        }

        await allProcessingTasks.WaitAsync(cancellationToken);
        ReportProgress("[live] All send lanes drained.");
        diagnostics.CompleteSendWindow();
        ReportProgress($"[live] Completed {processedCityObjectCount} city objects.");
        ReportProgress(
            $"[live] Send summary: attempted={processedCityObjectCount} sent={processedCityObjectCount}.");

        return [$"{endpoint}#{sceneAnchor?.SlotId ?? datasetRootSlot?.SlotId ?? string.Empty}"];
    }

    public async ValueTask DisposeAsync()
    {
        if (cityObjectChannels is not null)
        {
            foreach (Channel<QueuedCityObject> channel in cityObjectChannels)
            {
                channel.Writer.TryComplete();
            }
        }

        CancelProcessing();

        bool processingTasksDrained = true;
        if (processingTasks is not null)
        {
            Task[] drainTasks = processingTasks
                .Select(static task => task.ContinueWith(
                    static completedTask => _ = completedTask.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default))
                .ToArray();
            try
            {
                await Task.WhenAll(drainTasks).WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch (TimeoutException)
            {
                processingTasksDrained = false;
            }
        }

        if (!processingTasksDrained)
        {
            ReportProgress("[live][warn] DisposeAsync timed out waiting for send lanes. Disposing clients anyway.");
        }

        try
        {
            setupClient?.Dispose();

            if (backgroundClients is not null)
            {
                foreach (IResoniteLinkClient client in backgroundClients)
                {
                    client.Dispose();
                }
            }
        }
        finally
        {
            setupClient = null;
            backgroundClients = null;
            metadata = null;
            datasetContentSource = null;
            datasetRootSlot = null;
            datasetAssetsRootSlot = null;
            commonAssetsRootSlot = null;
            materialAssetManager = null;
            generatedAssetsRoot = null;
            sharedSlotCache.Clear();
            importedTextureUriCache = null;
            dispatchLaneAllocator = null;
            textureImportResolver = null;
            cityObjectChannels = null;
            processingTasks = null;
            processingCancellationSource?.Dispose();
            processingCancellationSource = null;
            firstProcessingFailureSource = null;
            sceneBuildStopwatch = null;
            sceneAnchor = null;
        }
    }

    private async Task ProcessQueuedCityObjectAsync(
        IResoniteLinkClient client,
        QueuedCityObject queuedCityObject,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(setupClient is null, this);
        PreparedCityObject preparedCityObject = await queuedCityObject.PreparationTask.WaitAsync(cancellationToken);
        await BuildPreparedCityObjectAsync(setupClient, client, preparedCityObject, cancellationToken);

        int processedCount = Interlocked.Increment(ref processedCityObjectCount);
        ReportProgress(
            $"[live] Sent city object {processedCount}: "
            + $"{preparedCityObject.CityObject.DisplayName} "
            + $"({preparedCityObject.CityObject.PackageName}/{preparedCityObject.CityObject.SlotKey})");
    }

    private void ReportProgress(string message)
    {
        PlateauLogLevel defaultLevel = message.StartsWith("[live]", StringComparison.Ordinal)
            ? PlateauLogLevel.Debug
            : PlateauLogLevel.Info;
        progressReporter?.Invoke(PlateauLog.NormalizeLegacyMessage(message, defaultLevel));
    }

    private Task<PreparedCityObject> CreatePreparationTask(
        ResoniteConstructionCityObject cityObject,
        CancellationToken callerCancellationToken)
    {
        CancellationToken processingCancellationToken = processingCancellationSource?.Token ?? CancellationToken.None;
        return PrepareCityObjectWithLinkedCancellationAsync(
            cityObject,
            callerCancellationToken,
            processingCancellationToken);
    }

    private async Task<PreparedCityObject> PrepareCityObjectWithLinkedCancellationAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken callerCancellationToken,
        CancellationToken processingCancellationToken)
    {
        using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            callerCancellationToken,
            processingCancellationToken);
        return await PrepareCityObjectAsync(cityObject, linkedCancellation.Token);
    }

    private async Task<PreparedCityObject> PrepareCityObjectAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(metadata is null, this);
        ObjectDisposedException.ThrowIf(textureImportResolver is null, this);
        (string TexturePath, ResoniteTextureSourceKind TextureSourceKind)[] distinctTextures = cityObject.Materials
            .Where(static material => !string.IsNullOrWhiteSpace(material.TexturePath))
            .Select(static material => (TexturePath: material.TexturePath!, TextureSourceKind: material.TextureSourceKind))
            .Distinct()
            .OrderBy(static texture => texture.TextureSourceKind)
            .ThenBy(static texture => texture.TexturePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Task<PreparedTextureReference>[] texturePreparationTasks = distinctTextures
            .Select(async texture =>
            {
                ResoniteTextureImport textureImport = await textureImportResolver.ResolveAsync(
                    texture.TexturePath,
                    texture.TextureSourceKind,
                    cancellationToken);

                return new PreparedTextureReference(
                    texture.TexturePath,
                    texture.TextureSourceKind,
                    textureImport);
            })
            .ToArray();
        Task<PreparedConstructionGeometry> geometryPreparationTask = cityObject.Geometry switch
        {
            ResoniteTriangleMeshGeometry triangleMesh => Task.Run<PreparedConstructionGeometry>(
                () => new PreparedTriangleMeshGeometry(ResoniteMeshImportFactory.Create(triangleMesh.Mesh)),
                cancellationToken),
            ResoniteHeightMapGridGeometry heightMap => Task.Run<PreparedConstructionGeometry>(
                () => new PreparedHeightMapGridGeometry(heightMap, PrepareHeightMapTexture(heightMap)),
                cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported geometry type '{cityObject.Geometry.GetType().Name}'."),
        };
        Stopwatch stopwatch = Stopwatch.StartNew();
        PreparedTextureReference[] preparedTextures = await Task.WhenAll(texturePreparationTasks);
        PreparedConstructionGeometry preparedGeometry = await geometryPreparationTask;
        stopwatch.Stop();
        diagnostics.RecordPrepare(cityObject.PackageName, stopwatch.Elapsed.TotalSeconds);

        if (Interlocked.CompareExchange(ref firstPreparedCityObjectLogged, 1, 0) == 0)
        {
            ReportProgress(
                $"[live] First city object prepared in {stopwatch.Elapsed.TotalSeconds:F3}s "
                + $"after scene start {GetSceneElapsedSeconds():F3}s: "
                + $"{cityObject.DisplayName} "
                + $"(textures={preparedTextures.Length}, geometry={DescribePreparedGeometry(preparedGeometry)})");
        }

        return new PreparedCityObject(
            cityObject,
            preparedGeometry,
            preparedTextures);
    }

    private static string ResolveCompletionMeshCode(ResoniteConstructionMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        string meshCode = metadata.Request.MeshCode;
        if (PlateauMeshCode.TryGetCenter(meshCode, out _))
        {
            return meshCode;
        }

        string? requestedMeshCode = metadata.SourceDataset.RequestedMeshCodes?
            .FirstOrDefault(static candidate => PlateauMeshCode.TryGetCenter(candidate, out _));
        if (!string.IsNullOrWhiteSpace(requestedMeshCode))
        {
            return requestedMeshCode;
        }

        throw new InvalidOperationException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Live Offset V2 requires a concrete meshcode anchor, but '{meshCode}' did not resolve to any concrete meshcode."));
    }

    private async Task BuildPreparedCityObjectAsync(
        IResoniteLinkClient mutationClient,
        IResoniteLinkClient importClient,
        PreparedCityObject preparedCityObject,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(metadata is null, this);
        ObjectDisposedException.ThrowIf(datasetRootSlot is null, this);
        ObjectDisposedException.ThrowIf(datasetAssetsRootSlot is null, this);
        ObjectDisposedException.ThrowIf(commonAssetsRootSlot is null, this);
        ObjectDisposedException.ThrowIf(materialAssetManager is null, this);
        ObjectDisposedException.ThrowIf(sceneAnchor is null, this);

        ResoniteConstructionCityObject cityObject = preparedCityObject.CityObject;
        using ResoniteLinkSendDiagnostics.CityObjectSendScope sendScope = diagnostics.BeginCityObjectSend(cityObject.PackageName);
        ReportBuildStep(cityObject, "Creating object slot hierarchy.");
        ObjectSlotHierarchy objectSlots = await CreateObjectSlotHierarchyAsync(
            mutationClient,
            datasetRootSlot.Value,
            datasetAssetsRootSlot.Value,
            cityObject,
            cancellationToken);

        ReportBuildStep(cityObject, $"Creating geometry component ({DescribePreparedGeometry(preparedCityObject.Geometry)}).");
        GeometryAssetBuildResult geometryBuild = await CreateGeometryComponentAsync(
            mutationClient,
            importClient,
            objectSlots,
            cityObject,
            preparedCityObject,
            cancellationToken);

        Dictionary<TextureReferenceKey, ResoniteTextureImport> preparedTextureDataByKey = preparedCityObject.Textures.ToDictionary(
            static texture => ResoniteMaterialAssetManager.CreateTextureReferenceKey(
                texture.TexturePath,
                texture.TextureSourceKind),
            static texture => texture.TextureImport);
        List<string> materialIds = [];
        List<string?> materialPropertyBlockIds = [];
        for (int materialIndex = 0; materialIndex < cityObject.Materials.Count; materialIndex++)
        {
            ResoniteMaterialBinding material = cityObject.Materials[materialIndex];
            ReportBuildStep(
                cityObject,
                $"Creating material {materialIndex + 1}/{cityObject.Materials.Count} ({material.MaterialKey}).");
            CreatedMaterialAsset materialAsset = await CreateMaterialComponentAsync(
                importClient,
                material,
                preparedTextureDataByKey,
                objectSlots with
                {
                    MeshAssetSlot = geometryBuild.MeshAssetSlot,
                    HeightMapAssetSlot = geometryBuild.HeightMapAssetSlot,
                },
                cancellationToken);
            materialIds.Add(materialAsset.MaterialComponentId);
            materialPropertyBlockIds.Add(materialAsset.MaterialPropertyBlockComponentId);
        }

        ReportBuildStep(cityObject, "Creating object presentation slot and components.");
        await CreatePresentationComponentsAsync(
            mutationClient,
            objectSlots,
            cityObject,
            geometryBuild.GeometryComponent,
            materialIds,
            materialPropertyBlockIds,
            cancellationToken);

        ReportBuildStep(cityObject, "Live build completed.");
        sendScope.MarkSent();

        if (Interlocked.CompareExchange(ref firstBuiltCityObjectLogged, 1, 0) == 0)
        {
            ReportProgress(
                $"[live] First city object built after {GetSceneElapsedSeconds():F3}s: "
                + $"{cityObject.DisplayName} ({cityObject.PackageName}/{cityObject.SlotKey})");
        }
    }

    private async Task<ObjectSlotHierarchy> CreateObjectSlotHierarchyAsync(
        IResoniteLinkClient client,
        CreatedSlot datasetRoot,
        CreatedSlot datasetAssetsRoot,
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(metadata is null, this);
        string rootMeshCode = cityObject.ActualMeshCode;
        ResoniteFloat3 cityObjectLocalPosition = ResolveCityObjectLocalPosition(
            metadata.LocalOrigin,
            rootMeshCode,
            cityObject.Transform.Position);
        ResoniteFloat3 rootMeshCodePosition = ResolveMeshCodeRootPosition(rootMeshCode);
        CreatedSlot meshRootSlot = await GetOrCreateSharedChildSlotAsync(
            client,
            datasetRoot,
            rootMeshCode,
            rootMeshCodePosition,
            null,
            cancellationToken);
        CreatedSlot assetPackageSlot = await GetOrCreateSharedChildSlotAsync(
            client,
            datasetAssetsRoot,
            cityObject.PackageName,
            null,
            null,
            cancellationToken);
        CreatedSlot packageSlot = await GetOrCreateSharedChildSlotAsync(
            client,
            meshRootSlot,
            cityObject.PackageName,
            null,
            null,
            cancellationToken);
        CreatedSlot assetLodSlot = await GetOrCreateSharedChildSlotAsync(
            client,
            assetPackageSlot,
            FormatLodSlotName(cityObject.LodLevel),
            null,
            null,
            cancellationToken);
        CreatedSlot lodSlot = await GetOrCreateSharedChildSlotAsync(
            client,
            packageSlot,
            FormatLodSlotName(cityObject.LodLevel),
            null,
            null,
            cancellationToken);
        return new ObjectSlotHierarchy(
            meshRootSlot,
            assetPackageSlot,
            packageSlot,
            assetLodSlot,
            lodSlot,
            MeshAssetSlot: null,
            HeightMapAssetSlot: null,
            CityObjectSlotName: cityObject.DisplayName,
            CityObjectLocalPosition: cityObjectLocalPosition,
            CityObjectRotation: cityObject.Transform.Rotation);
    }

    private double GetSceneElapsedSeconds()
    {
        return sceneBuildStopwatch?.Elapsed.TotalSeconds ?? 0.0;
    }

    private static string CreateDispatchDependencyKey(ResoniteConstructionCityObject cityObject)
    {
        string objectIdentity = cityObject.SourceObjectKey ?? cityObject.SlotKey;
        string lodKey = cityObject.LodLevel.HasValue
            ? cityObject.LodLevel.Value.ToString(CultureInfo.InvariantCulture)
            : "none";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{cityObject.ActualMeshCode}|{cityObject.PackageName}|{lodKey}|{objectIdentity}");
    }

    private async Task<CreatedMaterialAsset> CreateMaterialComponentAsync(
        IResoniteLinkClient client,
        ResoniteMaterialBinding material,
        IReadOnlyDictionary<TextureReferenceKey, ResoniteTextureImport> preparedTextureDataByKey,
        ObjectSlotHierarchy objectSlots,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(metadata is null, this);
        ObjectDisposedException.ThrowIf(commonAssetsRootSlot is null, this);
        ObjectDisposedException.ThrowIf(materialAssetManager is null, this);

        bool useCommonMaterialAssets = ShouldUseCommonMaterialAssets(material);
        string materialScopeId = useCommonMaterialAssets
            ? commonAssetsRootSlot.Value.SlotId
            : objectSlots.MeshAssetSlot!.Value.SlotId;
        string? materialSlotParentId = useCommonMaterialAssets ? commonAssetsRootSlot.Value.SlotId : null;
        string materialSlotName = CreateMaterialSlotName(material, useCommonMaterialAssets);
        return await materialAssetManager.CreateMaterialComponentAsync(
            client,
            material,
            preparedTextureDataByKey,
            materialScopeId,
            materialSlotParentId,
            materialSlotName,
            objectSlots.AssetLodSlot.SlotId,
            objectSlots.MeshAssetSlot?.SlotId ?? objectSlots.AssetLodSlot.SlotId,
            cancellationToken);
    }

    private static bool ShouldUseCommonMaterialAssets(ResoniteMaterialBinding material)
    {
        return material.TextureSourceKind == ResoniteTextureSourceKind.Bundled
            && !string.IsNullOrWhiteSpace(material.TexturePath)
            && !IsGeneratedDemTexturePath(material.TexturePath)
            && material.TexturePath.StartsWith("default-materials/", StringComparison.Ordinal);
    }

    private static string CreateMaterialSlotName(ResoniteMaterialBinding material, bool useCommonMaterialAssets)
    {
        ArgumentNullException.ThrowIfNull(material);

        string componentKind = material.MaterialType switch
        {
            ResoniteMaterialType.Standard => material.Projection switch
            {
                ResoniteMaterialProjection.Uv => "pbs-uv",
                ResoniteMaterialProjection.Triplanar => "pbs-triplanar",
                _ => "material",
            },
            ResoniteMaterialType.VertexColor => "vertex-color",
            ResoniteMaterialType.Wireframe => "wireframe",
            _ => "material",
        };

        string projectionName = material.Projection switch
        {
            ResoniteMaterialProjection.Uv => "uv",
            ResoniteMaterialProjection.Triplanar => "triplanar",
            _ => material.Projection.ToString().ToLowerInvariant(),
        };

        string sourceName = material.TexturePath is not null
            ? IsGeneratedDemTexturePath(material.TexturePath)
                ? "dem-overlay"
                : CreateTextureSourceToken(material.TexturePath)
            : material.MaterialType.ToString();

        string familyName = string.IsNullOrWhiteSpace(material.Family)
            ? "none"
            : material.Family!;
        string colorName = CreateCompactColorSuffix(material.BaseColor);
        string scaleName = material.TextureScale is not null
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{material.TextureScale.X:0.######}x{material.TextureScale.Y:0.######}")
            : "none";
        string offsetName = material.TextureOffset is not null
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{material.TextureOffset.X:0.######}x{material.TextureOffset.Y:0.######}")
            : "none";
        string depthName = material.DepthOffset is not null
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{material.DepthOffset.Factor:0.######}x{material.DepthOffset.Units:0.######}")
            : "none";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{componentKind}_{projectionName}_{sourceName}_{familyName}_{scaleName}_{offsetName}_{depthName}_{colorName}");
    }

    private static string CreateCompactColorSuffix(ResoniteColor color)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{color.R:0.###}-{color.G:0.###}-{color.B:0.###}-{color.A:0.###}");
    }

    private static string CreateTextureSourceToken(string texturePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(texturePath);

        string normalizedPath = texturePath.Replace('\\', '/').Trim('/');
        string? directoryName = Path.GetDirectoryName(normalizedPath)?.Replace('\\', '/').Trim('/');
        string fileStem = Path.GetFileNameWithoutExtension(normalizedPath);

        string normalizedStemPath = string.IsNullOrWhiteSpace(directoryName)
            ? fileStem
            : $"{directoryName}/{fileStem}";

        return normalizedStemPath.Replace('/', '_');
    }

    private static bool IsDemPackage(string packageName)
    {
        return string.Equals(packageName, DemPackageName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGeneratedDemTexturePath(string? texturePath)
    {
        return !string.IsNullOrWhiteSpace(texturePath)
            && texturePath.StartsWith(LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTexturePath, StringComparison.Ordinal);
    }


    private async Task<GeometryAssetBuildResult> CreateGeometryComponentAsync(
        IResoniteLinkClient mutationClient,
        IResoniteLinkClient importClient,
        ObjectSlotHierarchy objectSlots,
        ResoniteConstructionCityObject cityObject,
        PreparedCityObject preparedCityObject,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(metadata is null, this);

        return preparedCityObject.Geometry switch
        {
            PreparedTriangleMeshGeometry triangleMesh => await geometryAssetAssembler.CreateTriangleMeshAsync(
                mutationClient,
                importClient,
                objectSlots.AssetLodSlot.SlotId,
                CreateMeshAssetSlotName(cityObject),
                cityObject.DisplayName,
                triangleMesh.MeshImport,
                cancellationToken),
            PreparedHeightMapGridGeometry heightMap => await geometryAssetAssembler.CreateHeightMapGridAsync(
                mutationClient,
                importClient,
                objectSlots.AssetLodSlot.SlotId,
                CreateMeshAssetSlotName(cityObject),
                CreateHeightMapAssetSlotName(cityObject),
                cityObject.DisplayName,
                heightMap.Geometry,
                heightMap.HeightTextureImport,
                cancellationToken),
            _ => throw new InvalidOperationException(
                $"Unsupported prepared geometry type '{preparedCityObject.Geometry.GetType().Name}'."),
        };
    }

    private static ResoniteRawHdrTextureImport PrepareHeightMapTexture(ResoniteHeightMapGridGeometry geometry)
    {
        float[] rawPixels = new float[geometry.Width * geometry.Height * 4];
        double heightRange = Math.Max(geometry.MaxHeight - geometry.MinHeight, 0.0);

        for (int y = 0; y < geometry.Height; y++)
        {
            for (int x = 0; x < geometry.Width; x++)
            {
                // FrooxEngine.GridMesh uses `color.r + color.g + color.b / 3` for displacement.
                // Encode the inverted height into blue only (scaled by 3) so the effective sampled height stays 1x.
                double heightSample = geometry.HeightSamples[(y * geometry.Width) + x];
                double normalizedHeight = heightRange <= 1e-9
                    ? 0.0
                    : Math.Clamp((heightSample - geometry.MinHeight) / heightRange, 0.0, 1.0);
                float heightValue = (float)(1.0 - normalizedHeight);
                int pixelIndex = (y * geometry.Width * 4) + (x * 4);
                rawPixels[pixelIndex] = 0.0f;
                rawPixels[pixelIndex + 1] = 0.0f;
                rawPixels[pixelIndex + 2] = heightValue * 3.0f;
                rawPixels[pixelIndex + 3] = 1.0f;
            }
        }

        byte[] rawBytes = new byte[rawPixels.Length * sizeof(float)];
        Buffer.BlockCopy(rawPixels, 0, rawBytes, 0, rawBytes.Length);
        return new ResoniteRawHdrTextureImport(geometry.Width, geometry.Height, rawBytes);
    }

    private static async Task CreatePresentationComponentsAsync(
        IResoniteLinkClient client,
        ObjectSlotHierarchy objectSlots,
        ResoniteConstructionCityObject cityObject,
        CreatedComponent geometryComponent,
        IReadOnlyList<string> materialIds,
        IReadOnlyList<string?> materialPropertyBlockIds,
        CancellationToken cancellationToken)
    {
        Dictionary<string, Member> meshRendererMembers = new(StringComparer.Ordinal)
        {
            ["Mesh"] = new Reference
            {
                TargetID = geometryComponent.ComponentId,
            },
            ["Materials"] = new SyncList
            {
                Elements = materialIds
                    .Select(materialId => (Member)new Reference
                    {
                        TargetID = materialId,
                    })
                    .ToList(),
            },
        };
        if (materialPropertyBlockIds.Any(static propertyBlockId => propertyBlockId is not null))
        {
            meshRendererMembers["MaterialPropertyBlocks"] = new SyncList
            {
                Elements = materialPropertyBlockIds
                    .Select(static propertyBlockId => propertyBlockId is null
                        ? (Member)new EmptyElement()
                        : new Reference
                        {
                            TargetID = propertyBlockId,
                        })
                    .ToList(),
            };
        }

        CreatedSlot createdPresentationSlot = await CreateSlotCoreAsync(
            client,
            objectSlots.LodSlot.SlotId,
            objectSlots.CityObjectSlotName,
            objectSlots.CityObjectLocalPosition,
            objectSlots.CityObjectRotation,
            cancellationToken);

        await CreateComponentAsync(
            client,
            createdPresentationSlot.SlotId,
            "[FrooxEngine]FrooxEngine.MeshRenderer",
            meshRendererMembers,
            cancellationToken);
        await CreateComponentAsync(
            client,
            createdPresentationSlot.SlotId,
            "[FrooxEngine]FrooxEngine.MeshCollider",
            new Dictionary<string, Member>(StringComparer.Ordinal)
            {
                ["Type"] = new Field_Enum
                {
                    Value = cityObject.CollisionEnabled ? "Static" : "NoCollision",
                },
                ["CharacterCollider"] = new Field_bool
                {
                    Value = cityObject.CollisionEnabled,
                },
                ["Mesh"] = new Reference
                {
                    TargetID = geometryComponent.ComponentId,
                },
            },
            cancellationToken);
        for (int attempt = 1; attempt <= VisibilityPollAttemptLimit; attempt++)
        {
            Slot? presentationSlot = await client.GetSlotAsync(createdPresentationSlot.SlotId, 0, cancellationToken);
            IReadOnlyList<Component> presentationComponents = presentationSlot?.Components ?? [];
            bool hasMeshRenderer = presentationComponents.Any(component =>
                string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal));
            bool hasMeshCollider = presentationComponents.Any(component =>
                string.Equals(component.ComponentType, "[FrooxEngine]FrooxEngine.MeshCollider", StringComparison.Ordinal));
            if (hasMeshRenderer && hasMeshCollider)
            {
                return;
            }

            if (attempt < VisibilityPollAttemptLimit)
            {
                await Task.Delay(VisibilityPollDelayMilliseconds, cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Presentation slot '{createdPresentationSlot.SlotId}' is missing required presentation components after batch creation.");
    }

    private void ReportBuildStep(ResoniteConstructionCityObject cityObject, string step)
    {
        ReportProgress(
            $"[live] Building '{cityObject.DisplayName}' ({cityObject.PackageName}/{cityObject.SlotKey}): {step}");
    }

    private static string DescribePreparedGeometry(PreparedConstructionGeometry geometry)
    {
        return geometry switch
        {
            PreparedTriangleMeshGeometry triangleMesh =>
                $"triangle-mesh(vertices={triangleMesh.MeshImport.VertexCount}, submeshes={triangleMesh.MeshImport.Submeshes.Count})",
            PreparedHeightMapGridGeometry heightMap =>
                $"heightmap-grid({heightMap.Geometry.Width}x{heightMap.Geometry.Height})",
            _ => geometry.GetType().Name,
        };
    }

    private static Field_float3 CreateFloat3(ResoniteFloat3 value)
    {
        return new Field_float3
        {
            Value = new float3
            {
                x = (float)value.X,
                y = (float)value.Y,
                z = (float)value.Z,
            },
        };
    }

    private static Field_floatQ CreateFloatQ(ResoniteFloatQ value)
    {
        return new Field_floatQ
        {
            Value = new floatQ
            {
                x = (float)value.X,
                y = (float)value.Y,
                z = (float)value.Z,
                w = (float)value.W,
            },
        };
    }

    private static ResoniteFloat3 NormalizeMeshRootPosition(ResoniteFloat3 position)
    {
        return new ResoniteFloat3(position.X, 0.0, position.Z);
    }

    private static ResoniteFloat3 Add(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return new ResoniteFloat3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    }

    private static ResoniteFloat3 Subtract(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return new ResoniteFloat3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    }

    private static ResoniteFloat3 ResolveCityObjectLocalPosition(
        ResoniteLocalOrigin requestOrigin,
        string rootMeshCode,
        ResoniteFloat3 cityObjectPosition)
    {
        if (!PlateauMeshCode.TryGetCenter(rootMeshCode, out ResoniteLocalOrigin rootMeshCenter))
        {
            return cityObjectPosition;
        }

        // City objects are produced in the request-local-origin frame; convert them
        // to the target mesh-code local frame because root mesh slots already carry
        // the inter-mesh-code offset in Resonite.
        ResoniteFloat3 rootOffsetFromRequest = ComputeOriginOffset(requestOrigin, rootMeshCenter);
        return Subtract(cityObjectPosition, rootOffsetFromRequest);
    }

    private static bool TryGetMeshCodeName(Slot slot, out string meshCode)
    {
        meshCode = slot.Name?.Value ?? string.Empty;
        return PlateauMeshCode.TryGetCenter(meshCode, out _);
    }

    private static ResoniteFloat3 ComputeMeshCodeOffset(string referenceMeshCode, string meshCode)
    {
        if (!PlateauMeshCode.TryGetCenter(referenceMeshCode, out ResoniteLocalOrigin referenceCenter)
            || !PlateauMeshCode.TryGetCenter(meshCode, out ResoniteLocalOrigin currentCenter))
        {
            return new ResoniteFloat3(0.0, 0.0, 0.0);
        }

        return ComputeOriginOffset(referenceCenter, currentCenter);
    }

    private static ResoniteFloat3 ComputeOriginOffset(
        ResoniteLocalOrigin referenceCenter,
        ResoniteLocalOrigin currentCenter)
    {
        LocalCartesian cartesian = new(
            referenceCenter.Latitude,
            referenceCenter.Longitude,
            referenceCenter.Altitude,
            Geocentric.WGS84);
        (double x, double y, double z) eun = cartesian.Forward(
            currentCenter.Latitude,
            currentCenter.Longitude,
            currentCenter.Altitude);
        return new ResoniteFloat3(
            X: eun.x,
            // Mesh-root offsets should only reposition neighboring imports in-plane.
            // Keeping the local tangent frame's vertical component here introduces a
            // false Y drift between DEM parent meshes and detailed meshes.
            Y: 0.0,
            Z: eun.y);
    }

    private ResoniteFloat3 ResolveMeshCodeRootPosition(string meshCode)
    {
        SceneAnchor? anchor = sceneAnchor;
        if (anchor is null)
        {
            return new ResoniteFloat3(0.0, 0.0, 0.0);
        }

        if (string.Equals(anchor.Value.MeshCode, meshCode, StringComparison.Ordinal))
        {
            return anchor.Value.Position;
        }

        return Add(anchor.Value.Position, ComputeMeshCodeOffset(anchor.Value.MeshCode, meshCode));
    }

    private async Task AwaitProcessingTasksIfCompletedAsync()
    {
        if (firstProcessingFailureSource?.Task.IsCompletedSuccessfully == true)
        {
            Exception failure = await firstProcessingFailureSource.Task;
            throw failure;
        }

        if (processingTasks is not null && Array.Exists(processingTasks, static task => task.IsCompleted))
        {
            await Task.WhenAll(processingTasks);
        }
    }

    private void TryMarkProcessingFailure(Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            return;
        }

        firstProcessingFailureSource?.TrySetResult(exception);
    }

    private void CancelProcessing()
    {
        try
        {
            processingCancellationSource?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task<SceneAnchor> ResolveSceneAnchorAsync(
        IResoniteLinkClient client,
        CreatedSlot datasetRoot,
        string completionMeshCode,
        bool datasetRootExisted,
        CancellationToken cancellationToken)
    {
        await WaitForSlotAvailableAsync(client, datasetRoot.SlotId, cancellationToken);
        Slot? lastVisibleReferenceMeshRoot = null;
        int attemptLimit = datasetRootExisted ? VisibilityPollAttemptLimit : 1;
        for (int attempt = 1; attempt <= attemptLimit; attempt++)
        {
            Slot? datasetRootSnapshot = await client.GetSlotAsync(datasetRoot.SlotId, 1, cancellationToken);
            CreatedSlot? existingCompletionRoot = TryFindUniqueChildSlotByName(datasetRootSnapshot, completionMeshCode);
            if (existingCompletionRoot is not null)
            {
                await WaitForSlotAvailableAsync(client, existingCompletionRoot.Value.SlotId, cancellationToken);
                Slot? completionSlot = await client.GetSlotAsync(existingCompletionRoot.Value.SlotId, 0, cancellationToken);
                return new SceneAnchor(
                    existingCompletionRoot.Value.SlotId,
                    completionMeshCode,
                    completionSlot is null ? new ResoniteFloat3(0.0, 0.0, 0.0) : GetSlotPosition(completionSlot));
            }

            Slot? referenceMeshRoot = datasetRootSnapshot?.Children?
                .FirstOrDefault(static child => TryGetMeshCodeName(child, out _));
            if (referenceMeshRoot is not null)
            {
                lastVisibleReferenceMeshRoot = referenceMeshRoot;
            }

            if (!datasetRootExisted)
            {
                break;
            }

            if (attempt < attemptLimit)
            {
                await Task.Delay(VisibilityPollDelayMilliseconds, cancellationToken);
            }
        }

        ResoniteFloat3 anchorPosition = lastVisibleReferenceMeshRoot is null
            ? new ResoniteFloat3(0.0, 0.0, 0.0)
            : Add(
                GetSlotPosition(lastVisibleReferenceMeshRoot),
                ComputeMeshCodeOffset(lastVisibleReferenceMeshRoot.Name!.Value, completionMeshCode));
        CreatedSlot createdAnchor = await GetOrCreateSharedChildSlotAsync(
            client,
            datasetRoot,
            completionMeshCode,
            anchorPosition,
            null,
            cancellationToken);
        return new SceneAnchor(createdAnchor.SlotId, completionMeshCode, anchorPosition);
    }

    private static ResoniteFloat3 GetSlotPosition(Slot slot)
    {
        if (slot.Position is Field_float3 position)
        {
            return new ResoniteFloat3(position.Value.x, position.Value.y, position.Value.z);
        }

        return new ResoniteFloat3(0.0, 0.0, 0.0);
    }

    private static Task<CreatedSlot> CreateSlotAsync(
        IResoniteLinkClient client,
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        CancellationToken cancellationToken)
    {
        return CreateSlotCoreAsync(client, parentId, slotName, position, rotation, cancellationToken);
    }

    private Task<CreatedSlot> GetOrCreateSharedChildSlotAsync(
        IResoniteLinkClient client,
        CreatedSlot parent,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        CancellationToken cancellationToken)
    {
        return GetOrCreateSharedChildSlotByIdAsync(client, parent.SlotId, slotName, position, rotation, cancellationToken);
    }

    private async Task<CreatedSlot> GetOrCreateSharedChildSlotByIdAsync(
        IResoniteLinkClient client,
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        CancellationToken cancellationToken)
    {
        return await sharedSlotCache.GetOrCreateAsync(
            (parentId, slotName),
            ct => GetOrCreateSharedChildSlotCoreAsync(
                client,
                parentId,
                slotName,
                position,
                rotation,
                ct),
            cancellationToken);
    }

    private static async Task<CreatedSlot> GetOrCreateSharedChildSlotCoreAsync(
        IResoniteLinkClient client,
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        CancellationToken cancellationToken)
    {
        await WaitForSlotAvailableAsync(client, parentId, cancellationToken);
        CreatedSlot? existingSlot = await TryGetUniqueChildSlotByNameAsync(
            client,
            parentId,
            slotName,
            cancellationToken);
        if (existingSlot is not null)
        {
            return existingSlot.Value;
        }

        return await CreateSlotCoreAsync(client, parentId, slotName, position, rotation, cancellationToken);
    }

    private static async Task<CreatedComponent> CreateComponentAsync(
        IResoniteLinkClient client,
        string containerSlotId,
        string componentType,
        IReadOnlyDictionary<string, Member> members,
        CancellationToken cancellationToken)
    {
        await WaitForSlotAvailableAsync(client, containerSlotId, cancellationToken);
        string responseComponentId = await client.AddComponentAsync(
            CreateAddComponentOperation(containerSlotId, componentType, members),
            cancellationToken);
        return new CreatedComponent(responseComponentId, componentType);
    }

    private Task<CreatedComponent> CreateSharedAssetComponentAsync(
        IResoniteLinkClient client,
        string containerSlotId,
        string componentType,
        Func<CancellationToken, Task<Uri>> importAssetAsync,
        CancellationToken cancellationToken)
    {
        return CreateAssetComponentAsync(
            client,
            containerSlotId,
            componentType,
            new Dictionary<string, Member>(StringComparer.Ordinal),
            importAssetAsync,
            cancellationToken);
    }

    private Task<CreatedComponent> CreateDedicatedAssetComponentAsync(
        IResoniteLinkClient client,
        string containerSlotId,
        string componentType,
        Func<CancellationToken, Task<Uri>> importAssetAsync,
        CancellationToken cancellationToken)
    {
        return CreateAssetComponentAsync(
            client,
            containerSlotId,
            componentType,
            new Dictionary<string, Member>(StringComparer.Ordinal),
            importAssetAsync,
            cancellationToken);
    }

    private async Task<CreatedComponent> CreateAssetComponentAsync(
        IResoniteLinkClient client,
        string containerSlotId,
        string componentType,
        IReadOnlyDictionary<string, Member> members,
        Func<CancellationToken, Task<Uri>> importAssetAsync,
        CancellationToken cancellationToken)
    {
        ReportProgress($"[live] Importing asset for component type '{componentType}'.");
        Uri assetUri = await importAssetAsync(cancellationToken);
        ReportProgress($"[live] Asset import completed for component type '{componentType}' -> '{assetUri}'.");
        Dictionary<string, Member> componentMembers = new(members, StringComparer.Ordinal)
        {
            ["URL"] = new Field_Uri
            {
                Value = assetUri,
            },
        };
        return await CreateComponentAsync(
            client,
            containerSlotId,
            componentType,
            componentMembers,
            cancellationToken);
    }

    private static async Task<CreatedSlot> CreateSlotCoreAsync(
        IResoniteLinkClient client,
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        CancellationToken cancellationToken)
    {
        string responseSlotId = await client.AddSlotAsync(
            CreateAddSlotOperation(parentId, slotName, position, rotation),
            cancellationToken);
        return new CreatedSlot(responseSlotId, slotName);
    }

    private static async Task WaitForSlotAvailableAsync(
        IResoniteLinkClient client,
        string slotId,
        CancellationToken cancellationToken)
    {
        if (string.Equals(slotId, RootSlotId, StringComparison.Ordinal))
        {
            return;
        }

        for (int attempt = 1; attempt <= VisibilityPollAttemptLimit; attempt++)
        {
            Slot? slot = await client.GetSlotAsync(slotId, 0, cancellationToken);
            if (slot is not null)
            {
                return;
            }

            if (attempt < VisibilityPollAttemptLimit)
            {
                await Task.Delay(VisibilityPollDelayMilliseconds, cancellationToken);
            }
        }

        throw new InvalidOperationException($"ResoniteLink did not surface slot '{slotId}'.");
    }

    private static async Task<CreatedSlot?> TryGetUniqueChildSlotByNameWithPollingAsync(
        IResoniteLinkClient client,
        string parentId,
        string slotName,
        int attemptLimit,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= attemptLimit; attempt++)
        {
            CreatedSlot? childSlot = await TryGetUniqueChildSlotByNameAsync(
                client,
                parentId,
                slotName,
                cancellationToken);
            if (childSlot is not null)
            {
                return childSlot.Value;
            }

            if (attempt < attemptLimit)
            {
                await Task.Delay(VisibilityPollDelayMilliseconds, cancellationToken);
            }
        }

        return null;
    }

    private static async Task<CreatedSlot?> TryGetUniqueChildSlotByNameAsync(
        IResoniteLinkClient client,
        string parentId,
        string slotName,
        CancellationToken cancellationToken)
    {
        Slot? parentSlot = await client.GetSlotAsync(parentId, 1, cancellationToken);
        return TryFindUniqueChildSlotByName(parentSlot, slotName, parentId);
    }

    private static CreatedSlot? TryFindUniqueChildSlotByName(
        Slot? parentSlot,
        string slotName,
        string? parentId = null)
    {
        return TryFindUniqueMatchingChildSlot(
            parentSlot,
            slotName,
            static _ => true,
            parentId);
    }

    private static CreatedSlot? TryFindUniqueMatchingChildSlot(
        Slot? parentSlot,
        string slotName,
        Func<Slot, bool> predicate,
        string? parentId = null)
    {
        if (parentSlot?.Children is null)
        {
            return null;
        }

        Slot[] matches = parentSlot.Children
            .Where(child => string.Equals(child.Name?.Value, slotName, StringComparison.Ordinal))
            .Where(predicate)
            .ToArray();
        if (matches.Length == 0)
        {
            return null;
        }

        if (matches.Length > 1)
        {
            string parentIdentifier = parentId ?? parentSlot.ID ?? "<unknown>";
            throw new InvalidOperationException(
                $"Parent slot '{parentIdentifier}' contains multiple child slots named '{slotName}'.");
        }

        string existingSlotId = matches[0].ID
            ?? throw new InvalidOperationException(
                $"Child slot '{slotName}' under parent '{parentId ?? parentSlot.ID ?? "<unknown>"}' did not surface an ID.");
        return new CreatedSlot(existingSlotId, slotName);
    }

    private async Task<Uri> ImportTextureAsync(
        IResoniteLinkClient client,
        ResoniteTextureImport textureImport,
        CancellationToken cancellationToken)
    {
        TextureImportCacheKey? cacheKey = TryCreateTextureImportCacheKey(textureImport);
        if (cacheKey is null)
        {
            return await client.ImportTextureAsync(textureImport, cancellationToken);
        }

        ObjectDisposedException.ThrowIf(importedTextureUriCache is null, this);
        return await importedTextureUriCache.GetOrCreateAsync(
            cacheKey.Value,
            ct => client.ImportTextureAsync(textureImport, ct),
            cancellationToken);
    }

    private static TextureImportCacheKey? TryCreateTextureImportCacheKey(ResoniteTextureImport textureImport)
    {
        return textureImport switch
        {
            ResoniteFileTextureImport fileImport => new TextureImportCacheKey("file", fileImport.AbsolutePath),
            ResoniteRawTextureImport rawImport when rawImport.Identity is not null => new TextureImportCacheKey(
                "raw",
                rawImport.Identity,
                rawImport.ColorProfile),
            _ => null,
        };
    }

    private static AddSlot CreateAddSlotOperation(
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation)
    {
        return new AddSlot
        {
            Data = new Slot
            {
                ID = null!,
                Parent = new Reference
                {
                    TargetID = parentId,
                },
                Name = new Field_string
                {
                    Value = slotName,
                },
                Position = position is null ? null : CreateFloat3(position),
                Rotation = rotation is null ? null : CreateFloatQ(rotation),
            },
        };
    }

    private static Dictionary<string, Member> CreateDatasetLicenseMembers(
        ResoniteLicenseComponentMetadata license)
    {
        return new Dictionary<string, Member>(StringComparer.Ordinal)
        {
            ["RequireCredit"] = new Field_bool
            {
                Value = license.RequireCredit,
            },
            ["CreditString"] = new Field_string
            {
                Value = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{license.CreditText} License: {license.LicenseName} ({license.LicenseUrl})"),
            },
        };
    }

    private static AddComponent CreateAddComponentOperation(
        string containerSlotId,
        string componentType,
        IReadOnlyDictionary<string, Member> members)
    {
        return new AddComponent
        {
            ContainerSlotId = containerSlotId,
            Data = new Component
            {
                ID = null!,
                ComponentType = componentType,
                Members = new Dictionary<string, Member>(members, StringComparer.Ordinal),
            },
        };
    }

    private static string FormatLodSlotName(int? lodLevel)
    {
        return lodLevel.HasValue
            ? string.Create(CultureInfo.InvariantCulture, $"LOD{lodLevel.Value}")
            : "LOD0";
    }

    private static string CreateMeshAssetSlotName(ResoniteConstructionCityObject cityObject)
    {
        return cityObject.DisplayName;
    }

    private static string CreateHeightMapAssetSlotName(ResoniteConstructionCityObject cityObject)
    {
        return string.Concat(CreateMeshAssetSlotName(cityObject), HeightMapAssetSlotSuffix);
    }

    internal readonly record struct CreatedSlot(
        string SlotId,
        string SlotName);

    internal readonly record struct CreatedComponent(
        string ComponentId,
        string ComponentType);


    private sealed record ObjectSlotHierarchy(
        CreatedSlot MeshRootSlot,
        CreatedSlot AssetPackageSlot,
        CreatedSlot PackageSlot,
        CreatedSlot AssetLodSlot,
        CreatedSlot LodSlot,
        CreatedSlot? MeshAssetSlot,
        CreatedSlot? HeightMapAssetSlot,
        string CityObjectSlotName,
        ResoniteFloat3 CityObjectLocalPosition,
        ResoniteFloatQ? CityObjectRotation);

    internal sealed class DispatchLaneAllocator
    {
        private readonly int connectionCount;
        private readonly ConcurrentDictionary<string, int> lanesByDependencyKey = new(StringComparer.Ordinal);
        private int nextLane = -1;

        public DispatchLaneAllocator(int connectionCount)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(connectionCount, 1);
            this.connectionCount = connectionCount;
        }

        public int GetLane(ResoniteConstructionCityObject cityObject)
        {
            ArgumentNullException.ThrowIfNull(cityObject);

            if (connectionCount == 1)
            {
                return 0;
            }

            string dependencyKey = CreateDispatchDependencyKey(cityObject);
            return lanesByDependencyKey.GetOrAdd(
                dependencyKey,
                _ => Interlocked.Increment(ref nextLane) % connectionCount);
        }
    }

    private sealed record QueuedCityObject(
        ResoniteConstructionCityObject CityObject,
        Task<PreparedCityObject> PreparationTask);

    private readonly record struct SceneAnchor(
        string SlotId,
        string MeshCode,
        ResoniteFloat3 Position);

    private abstract record PreparedConstructionGeometry;

    private sealed record PreparedTriangleMeshGeometry(
        ImportMeshRawData MeshImport)
        : PreparedConstructionGeometry;

    private sealed record PreparedHeightMapGridGeometry(
        ResoniteHeightMapGridGeometry Geometry,
        ResoniteRawHdrTextureImport HeightTextureImport)
        : PreparedConstructionGeometry;

    private sealed record PreparedCityObject(
        ResoniteConstructionCityObject CityObject,
        PreparedConstructionGeometry Geometry,
        IReadOnlyList<PreparedTextureReference> Textures)
    {
        public bool TryGetTextureImport(
            string texturePath,
            ResoniteTextureSourceKind textureSourceKind,
            out ResoniteTextureImport? textureImport)
        {
            PreparedTextureReference? preparedTexture = Textures.FirstOrDefault(texture =>
                string.Equals(texture.TexturePath, texturePath, StringComparison.Ordinal)
                && texture.TextureSourceKind == textureSourceKind);
            textureImport = preparedTexture?.TextureImport;
            return preparedTexture is not null;
        }
    }

    private sealed record PreparedTextureReference(
        string TexturePath,
        ResoniteTextureSourceKind TextureSourceKind,
        ResoniteTextureImport TextureImport);
}
