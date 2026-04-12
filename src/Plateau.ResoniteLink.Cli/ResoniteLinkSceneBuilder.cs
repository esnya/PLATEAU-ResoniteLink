using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
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
    private const int WorkerConnectTimeoutMilliseconds = 5000;
    private const string RootSlotId = "Root";
    private const string CommonAssetsSlotName = "Common";
    private const string DemPackageName = "dem";
    private const string HeightMapAssetSlotSuffix = "_heightmap";
    private const float DefaultNormalScale = 1.0f;
    private const float DefaultBundledHeightScale = 0.002f;
    private readonly Func<IResoniteLinkClient> clientFactory;
    private readonly Uri endpoint;
    private readonly int connectionCount;
    private readonly int importMeshTimeoutMilliseconds;
    private readonly ResoniteLinkSendDiagnostics diagnostics;
    private readonly ITerrainTextureAssetGenerator terrainTextureAssetGenerator;
    private readonly ResoniteGeometryAssetAssembler geometryAssetAssembler;
#pragma warning disable CA1859
    private readonly ILiveSendClientSession clientSession;
#pragma warning restore CA1859
    private readonly Action<string>? progressReporter;
    private readonly AsyncCompletedResultCache<(string ParentSlotId, string SlotName), CreatedSlot> sharedSlotCache = new();
#pragma warning disable CA1859
    private readonly IResoniteSceneAnchorResolver sceneAnchorResolver;
#pragma warning restore CA1859
#pragma warning disable CA1859
    private readonly IResoniteSceneBootstrapCoordinator sceneBootstrapCoordinator;
#pragma warning restore CA1859
    private ResoniteConstructionMetadata? metadata;
    private CreatedSlot? datasetRootSlot;
    private CreatedSlot? datasetAssetsRootSlot;
    private CreatedSlot? commonAssetsRootSlot;
    private ResoniteMaterialAssetManager? materialAssetManager;
    private string? runRoot;
    private AsyncCompletedResultCache<TextureImportCacheKey, Uri>? importedTextureUriCache;
    private DispatchLaneAllocator? dispatchLaneAllocator;
    private ResoniteTextureImportResolver? textureImportResolver;
    private Channel<QueuedCityObject>[]? cityObjectChannels;
    private Task[]? processingTasks;
    private CancellationTokenSource? processingCancellationSource;
    private TaskCompletionSource<Exception>? firstProcessingFailureSource;
    private FixedCellCityObjectMeshBaker? meshBaker;
    private int processedCityObjectCount;
    private Stopwatch? sceneBuildStopwatch;
    private int firstQueuedCityObjectLogged;
    private int firstPreparedCityObjectLogged;
    private int firstBuiltCityObjectLogged;
    private IPlateauDatasetContentSource? datasetContentSource;
    private SceneAnchor? sceneAnchor;

    public ResoniteLinkSceneBuilder(Uri endpoint, Action<string>? progressReporter = null)
        : this(endpoint, 4, ResoniteLinkSendDiagnostics.Disabled, static () => new ResoniteLinkClient(), new TerrainTextureAssetGenerator(), enableMeshBake: true, CliDefaultOptions.ResoniteLinkImportMeshTimeoutMilliseconds, progressReporter)
    {
    }

    public ResoniteLinkSceneBuilder(Uri endpoint, int connectionCount, Action<string>? progressReporter = null)
        : this(endpoint, connectionCount, ResoniteLinkSendDiagnostics.Disabled, static () => new ResoniteLinkClient(), new TerrainTextureAssetGenerator(), enableMeshBake: true, CliDefaultOptions.ResoniteLinkImportMeshTimeoutMilliseconds, progressReporter)
    {
    }

    internal ResoniteLinkSceneBuilder(
        Uri endpoint,
        int connectionCount,
        ResoniteLinkSendDiagnostics diagnostics,
        Action<string>? progressReporter = null)
        : this(endpoint, connectionCount, diagnostics, static () => new ResoniteLinkClient(), new TerrainTextureAssetGenerator(), enableMeshBake: true, CliDefaultOptions.ResoniteLinkImportMeshTimeoutMilliseconds, progressReporter)
    {
    }

    internal ResoniteLinkSceneBuilder(
        Uri endpoint,
        int connectionCount,
        ResoniteLinkSendDiagnostics diagnostics,
        bool enableMeshBake,
        int importMeshTimeoutMilliseconds = CliDefaultOptions.ResoniteLinkImportMeshTimeoutMilliseconds,
        Action<string>? progressReporter = null)
        : this(endpoint, connectionCount, diagnostics, static () => new ResoniteLinkClient(), new TerrainTextureAssetGenerator(), enableMeshBake, importMeshTimeoutMilliseconds, progressReporter)
    {
    }

    internal ResoniteLinkSceneBuilder(
        Uri endpoint,
        int connectionCount,
        ResoniteLinkSendDiagnostics diagnostics,
        Func<IResoniteLinkClient> clientFactory,
        ITerrainTextureAssetGenerator? terrainTextureAssetGenerator = null,
        bool enableMeshBake = true,
        int importMeshTimeoutMilliseconds = CliDefaultOptions.ResoniteLinkImportMeshTimeoutMilliseconds,
        Action<string>? progressReporter = null)
    {
        this.endpoint = endpoint;
        this.connectionCount = connectionCount;
        this.importMeshTimeoutMilliseconds = importMeshTimeoutMilliseconds;
        this.diagnostics = diagnostics;
        this.clientFactory = clientFactory;
        this.terrainTextureAssetGenerator = terrainTextureAssetGenerator ?? new TerrainTextureAssetGenerator();
        MeshBakeEnabled = enableMeshBake;
        this.progressReporter = progressReporter;
        sceneAnchorResolver = new ResoniteSceneAnchorResolver();
        sceneBootstrapCoordinator = new ResoniteSceneBootstrapCoordinator(
            GetOrCreateDatasetRootAsync,
            GetOrCreateSharedChildSlotAsync,
            CreateComponentAsync,
            sceneAnchorResolver);
        geometryAssetAssembler = new ResoniteGeometryAssetAssembler(ReportProgress);
        clientSession = new LiveSendClientSession(
            CreateConfiguredClient,
            endpoint,
            connectionCount,
            WorkerConnectTimeoutMilliseconds,
            ReportProgress);
    }

    internal bool MeshBakeEnabled { get; }

    public async Task EnsureConnectedAsync(
        PlateauImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await clientSession.EnsureSetupClientConnectedAsync(request, cancellationToken);
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
        runRoot = CreateRunRoot(resolvedWorkRoot);
        Directory.CreateDirectory(runRoot);
        ReportProgress(
            $"[live] Initializing scene state for dataset '{metadata.Request.Dataset}' "
            + $"mesh '{metadata.Request.MeshCode}' at '{resolvedWorkRoot}'.");
        ReportProgress(
            $"[live] Connecting setup ResoniteLink session to {endpoint} "
            + $"and scheduling {Math.Max(connectionCount - 1, 0)} worker session(s).");
        await clientSession.EnsureSetupClientConnectedAsync(metadata.Request, cancellationToken);
        ObjectDisposedException.ThrowIf(clientSession.SetupClient is null, this);
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

        ReportProgress("[live] Opening resolved dataset content source for texture imports.");
        datasetContentSource = await PlateauDatasetContentSourceFactory.CreateAsync(localSource.LocalSourcePath!, cancellationToken);
        textureImportResolver = new ResoniteTextureImportResolver(
            datasetContentSource,
            metadata.SourceDataset.TerrainTextureOverlays,
            terrainTextureAssetGenerator);
        ReportProgress("[live] Creating dataset root, asset groups, and anchor slots.");
        ObjectDisposedException.ThrowIf(clientSession.SetupClient is null, this);
        IResoniteLinkClient setupClient = clientSession.SetupClient;
        ResoniteSceneBootstrapState bootstrapState = await sceneBootstrapCoordinator.BootstrapAsync(
            setupClient,
            metadata,
            cancellationToken);
        datasetRootSlot = bootstrapState.DatasetRootSlot;
        datasetAssetsRootSlot = bootstrapState.DatasetAssetsRootSlot;
        commonAssetsRootSlot = bootstrapState.CommonAssetsRootSlot;
        sceneAnchor = bootstrapState.SceneAnchor;

        ReportProgress("[live] Dataset slots and asset groups are ready.");
        clientSession.BeginWorkerClientTracking();
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
        meshBaker = MeshBakeEnabled ? new FixedCellCityObjectMeshBaker() : null;
        diagnostics.StartSendWindow(connectionCount);
        processingCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        firstProcessingFailureSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        processingTasks = CreateProcessingTasks(metadata.Request, processingCancellationSource.Token);
        ReportProgress($"[live] Send lanes ready (setup=1, workers={Math.Max(connectionCount - 1, 0)}).");
    }

    private static async Task<(CreatedSlot Slot, bool Existed)> GetOrCreateDatasetRootAsync(
        IResoniteLinkClient client,
        string slotName,
        CancellationToken cancellationToken)
    {
        CreatedSlot? existingDatasetRoot = await TryGetUniqueChildSlotByNameAsync(
            client,
            RootSlotId,
            slotName,
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

    private IResoniteLinkClient CreateConfiguredClient()
    {
        IResoniteLinkClient client = new RetryingResoniteLinkClient(
            clientFactory,
            ReportProgress,
            importMeshTimeoutMilliseconds);
        return diagnostics.Enabled ? new MetricsResoniteLinkClient(client, diagnostics) : client;
    }

    private Task[] CreateProcessingTasks(
        PlateauImportRequest request,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(cityObjectChannels is null, this);

        Task[] tasks = new Task[connectionCount];
        for (int laneIndex = 0; laneIndex < connectionCount; laneIndex++)
        {
            int capturedLaneIndex = laneIndex;
            tasks[capturedLaneIndex] = ProcessQueuedCityObjectsOnLaneAsync(
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

    private async Task ProcessQueuedCityObjectsOnLaneAsync(
        ChannelReader<QueuedCityObject> reader,
        PlateauImportRequest request,
        int laneIndex,
        CancellationToken cancellationToken)
    {
        IResoniteLinkClient client = await clientSession.CreateLaneClientAsync(
            request,
            laneIndex,
            cancellationToken);
        try
        {
            if (laneIndex > 0)
            {
                ReportProgress(
                    $"[live] Connected worker ResoniteLink session {laneIndex + 1}/{connectionCount} "
                    + $"to {endpoint} for dataset '{request.Dataset}' mesh '{request.MeshCode}'.");
            }
            await ProcessQueuedCityObjectsAsync(reader, client, laneIndex, cancellationToken);
        }
        catch (Exception exception)
        {
            TryMarkProcessingFailure(exception);
            CancelProcessing();
            throw;
        }
    }

    public async Task ProcessCityObjectAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        ObjectDisposedException.ThrowIf(metadata is null, this);
        ObjectDisposedException.ThrowIf(cityObjectChannels is null, this);
        ObjectDisposedException.ThrowIf(processingTasks is null, this);

        if (meshBaker?.TryBuffer(cityObject, out ResoniteConstructionCityObject? bakedCityObject) == true)
        {
            if (bakedCityObject is null)
            {
                return;
            }

            cityObject = bakedCityObject;
        }

        await EnqueueCityObjectAsync(cityObject, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> CompleteAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(cityObjectChannels is null, this);
        ObjectDisposedException.ThrowIf(processingTasks is null, this);

        if (meshBaker is not null)
        {
            IReadOnlyList<ResoniteConstructionCityObject> bakedCityObjects = meshBaker.FlushAll();
            foreach (ResoniteConstructionCityObject bakedCityObject in bakedCityObjects)
            {
                await EnqueueCityObjectAsync(bakedCityObject, cancellationToken);
            }

            if (meshBaker.BakedOutputCityObjectCount > 0)
            {
                ReportProgress(
                    $"[live] MeshBake batched {meshBaker.BakedInputCityObjectCount} input city objects "
                    + $"into {meshBaker.BakedOutputCityObjectCount} baked cell batches.");
            }
        }

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
        ReportProgress(
            $"[live] Completed {processedCityObjectCount} city objects.");
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
            clientSession.DisposeClients();
        }
        finally
        {
            metadata = null;
            datasetContentSource = null;
            datasetRootSlot = null;
            datasetAssetsRootSlot = null;
            commonAssetsRootSlot = null;
            materialAssetManager = null;
            string? priorRunRoot = runRoot;
            runRoot = null;
            TryDeleteDirectory(priorRunRoot);
            sharedSlotCache.Clear();
            importedTextureUriCache = null;
            dispatchLaneAllocator = null;
            textureImportResolver = null;
            cityObjectChannels = null;
            processingTasks = null;
            meshBaker = null;
            processingCancellationSource?.Dispose();
            processingCancellationSource = null;
            firstProcessingFailureSource = null;
            sceneBuildStopwatch = null;
            sceneAnchor = null;
        }
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string CreateRunRoot(string datasetRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetRoot);
        return Path.Combine(Path.GetFullPath(datasetRoot), "run", Guid.NewGuid().ToString("N"));
    }

    private async Task ProcessQueuedCityObjectAsync(
        IResoniteLinkClient client,
        QueuedCityObject queuedCityObject,
        CancellationToken cancellationToken)
    {
        PreparedCityObject preparedCityObject = await queuedCityObject.PreparationTask.WaitAsync(cancellationToken);
        await BuildPreparedCityObjectAsync(client, preparedCityObject, cancellationToken);

        int processedCount = Interlocked.Increment(ref processedCityObjectCount);
        ReportProgress(
            $"[live] Sent city object {processedCount}: "
            + $"{preparedCityObject.CityObject.DisplayName} "
            + $"({preparedCityObject.CityObject.PackageName}/{preparedCityObject.CityObject.SlotKey})",
            PlateauLogLevel.Info);
    }

    private async Task EnqueueCityObjectAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken)
    {
        await AwaitProcessingTasksIfCompletedAsync();

        Task<PreparedCityObject> preparationTask = CreatePreparationTask(cityObject, cancellationToken);
        if (Interlocked.CompareExchange(ref firstQueuedCityObjectLogged, 1, 0) == 0)
        {
            ReportProgress(
                $"[live] First city object queued after {GetSceneElapsedSeconds():F3}s: "
                + $"{cityObject.DisplayName} ({cityObject.PackageName}/{cityObject.SlotKey})");
        }

        ObjectDisposedException.ThrowIf(dispatchLaneAllocator is null, this);
        ObjectDisposedException.ThrowIf(cityObjectChannels is null, this);

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

    private void ReportProgress(string message)
    {
        ReportProgress(message, null);
    }

    private void ReportProgress(string message, PlateauLogLevel? defaultLevel)
    {
        PlateauLogLevel resolvedDefaultLevel = defaultLevel ?? PlateauLog.InferLegacyDefaultLevel(message);
        progressReporter?.Invoke(PlateauLog.NormalizeLegacyMessage(message, resolvedDefaultLevel));
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
        IResoniteLinkClient importClient,
        PreparedCityObject preparedCityObject,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(metadata is null, this);
        ObjectDisposedException.ThrowIf(clientSession.SetupClient is null, this);
        ObjectDisposedException.ThrowIf(datasetRootSlot is null, this);
        ObjectDisposedException.ThrowIf(datasetAssetsRootSlot is null, this);
        ObjectDisposedException.ThrowIf(commonAssetsRootSlot is null, this);
        ObjectDisposedException.ThrowIf(materialAssetManager is null, this);
        ObjectDisposedException.ThrowIf(sceneAnchor is null, this);

        IResoniteLinkClient mutationClient = clientSession.SetupClient;
        ResoniteConstructionCityObject cityObject = preparedCityObject.CityObject;
        using ResoniteLinkSendDiagnostics.CityObjectSendScope sendScope = diagnostics.BeginCityObjectSend(cityObject.PackageName);
        ReportBuildStep(cityObject, "Creating object slot hierarchy.");
        ObjectSlotHierarchy objectSlots = await CreateObjectSlotHierarchyAsync(
            mutationClient,
            datasetRootSlot.Value,
            datasetAssetsRootSlot.Value,
            cityObject,
            cancellationToken);

        ReportBuildStep(cityObject, $"Preparing geometry assets ({DescribePreparedGeometry(preparedCityObject.Geometry)}).");
        PreparedGeometryAssetBatch preparedGeometryBatch = await PrepareGeometryBatchAsync(
            importClient,
            cityObject,
            preparedCityObject,
            cancellationToken);

        Dictionary<TextureReferenceKey, ResoniteTextureImport> preparedTextureDataByKey = preparedCityObject.Textures.ToDictionary(
            static texture => ResoniteMaterialAssetManager.CreateTextureReferenceKey(
                texture.TexturePath,
                texture.TextureSourceKind),
            static texture => texture.TextureImport);
        IReadOnlyList<MaterialReferenceTarget> materialTargets = await ResolveMaterialTargetsAsync(
            importClient,
            cityObject,
            preparedTextureDataByKey,
            objectSlots,
            cancellationToken);

        ReportBuildStep(cityObject, "Creating object-scoped DataModel batch.");
        await CreateCityObjectBatchAsync(
            mutationClient,
            importClient,
            objectSlots,
            cityObject,
            preparedTextureDataByKey,
            preparedGeometryBatch,
            materialTargets,
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
        CreatedSlot assetMeshRootSlot = await GetOrCreateSharedChildSlotAsync(
            client,
            datasetAssetsRoot,
            rootMeshCode,
            null,
            null,
            cancellationToken);
        CreatedSlot assetPackageSlot = await GetOrCreateSharedChildSlotAsync(
            client,
            assetMeshRootSlot,
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
            assetMeshRootSlot,
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
        ObjectDisposedException.ThrowIf(materialAssetManager is null, this);

        bool useCommonMaterialAssets = material.AssetScope == ResoniteMaterialAssetScope.Common;
        string materialScopeId = useCommonMaterialAssets
            ? commonAssetsRootSlot!.Value.SlotId
            : objectSlots.MeshAssetSlot!.Value.SlotId;
        string? materialSlotParentId = useCommonMaterialAssets ? commonAssetsRootSlot!.Value.SlotId : null;
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

    private async Task<IReadOnlyList<MaterialReferenceTarget>> ResolveMaterialTargetsAsync(
        IResoniteLinkClient importClient,
        ResoniteConstructionCityObject cityObject,
        IReadOnlyDictionary<TextureReferenceKey, ResoniteTextureImport> preparedTextureDataByKey,
        ObjectSlotHierarchy objectSlots,
        CancellationToken cancellationToken)
    {
        List<MaterialReferenceTarget> materialTargets = [];
        for (int materialIndex = 0; materialIndex < cityObject.Materials.Count; materialIndex++)
        {
            ResoniteMaterialBinding material = cityObject.Materials[materialIndex];
            ReportBuildStep(
                cityObject,
                $"Creating material {materialIndex + 1}/{cityObject.Materials.Count} ({material.MaterialKey}).");
            if (material.AssetScope == ResoniteMaterialAssetScope.Common)
            {
                CreatedMaterialAsset materialAsset = await CreateMaterialComponentAsync(
                    importClient,
                    material,
                    preparedTextureDataByKey,
                    objectSlots,
                    cancellationToken);
                materialTargets.Add(MaterialReferenceTarget.FromCanonical(materialAsset.MaterialComponentId));
            }
            else
            {
                materialTargets.Add(MaterialReferenceTarget.FromDedicatedMaterial(material));
            }
        }

        return materialTargets;
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


    private async Task<PreparedGeometryAssetBatch> PrepareGeometryBatchAsync(
        IResoniteLinkClient importClient,
        ResoniteConstructionCityObject cityObject,
        PreparedCityObject preparedCityObject,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(metadata is null, this);

        return preparedCityObject.Geometry switch
        {
            PreparedTriangleMeshGeometry triangleMesh => await geometryAssetAssembler.PrepareTriangleMeshAsync(
                importClient,
                CreateMeshAssetSlotName(cityObject),
                cityObject.DisplayName,
                triangleMesh.MeshImport,
                cancellationToken),
            PreparedHeightMapGridGeometry heightMap => await geometryAssetAssembler.PrepareHeightMapGridAsync(
                importClient,
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

    private async Task CreateCityObjectBatchAsync(
        IResoniteLinkClient client,
        IResoniteLinkClient importClient,
        ObjectSlotHierarchy objectSlots,
        ResoniteConstructionCityObject cityObject,
        IReadOnlyDictionary<TextureReferenceKey, ResoniteTextureImport> preparedTextureDataByKey,
        PreparedGeometryAssetBatch preparedGeometryBatch,
        IReadOnlyList<MaterialReferenceTarget> materialTargets,
        CancellationToken cancellationToken)
    {
        CityObjectBatchBuilder batchBuilder = new();
        PendingBatchSlot meshAssetSlot = batchBuilder.AddSlot(
            objectSlots.AssetLodSlot.SlotId,
            preparedGeometryBatch.MeshAssetSlotName,
            null,
            null);
        PendingBatchSlot? heightMapAssetSlot = null;
        PendingBatchComponent geometryComponent;

        switch (preparedGeometryBatch)
        {
            case PreparedTriangleMeshAssetBatch triangleMesh:
                geometryComponent = batchBuilder.AddComponent(
                    meshAssetSlot.LocalId,
                    "[FrooxEngine]FrooxEngine.StaticMesh",
                    new Dictionary<string, Member>(StringComparer.Ordinal)
                    {
                        ["URL"] = new Field_Uri
                        {
                            Value = triangleMesh.MeshUri,
                        },
                    });
                break;
            case PreparedHeightMapGridAssetBatch heightMap:
                heightMapAssetSlot = batchBuilder.AddSlot(
                    objectSlots.AssetLodSlot.SlotId,
                    heightMap.HeightMapAssetSlotName,
                    null,
                    null);
                PendingBatchComponent heightTexture = batchBuilder.AddComponent(
                    heightMapAssetSlot.Value.LocalId,
                    "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    ResoniteGeometryAssetAssembler.CreateHeightMapTextureMembers(heightMap.HeightTextureUri));
                double displacementMagnitude = Math.Max(heightMap.Geometry.MaxHeight - heightMap.Geometry.MinHeight, 0.0);
                ReportProgress(
                    $"[live] HeightMap texture ready. Creating GridMesh "
                    + $"({heightMap.Geometry.Width}x{heightMap.Geometry.Height}, displacement={displacementMagnitude:F3}).");
                geometryComponent = batchBuilder.AddComponent(
                    meshAssetSlot.LocalId,
                    "[FrooxEngine]FrooxEngine.GridMesh",
                    new Dictionary<string, Member>(StringComparer.Ordinal)
                    {
                        ["Points"] = new Field_int2
                        {
                            Value = new int2
                            {
                                x = heightMap.Geometry.Width,
                                y = heightMap.Geometry.Height,
                            },
                        },
                        ["Size"] = new Field_float2
                        {
                            Value = new float2
                            {
                                x = (float)heightMap.Geometry.Size.X,
                                y = (float)heightMap.Geometry.Size.Y,
                            },
                        },
                        ["DisplacementMagnitude"] = new Field_float
                        {
                            Value = (float)displacementMagnitude,
                        },
                        ["DisplacementTexture"] = new Reference
                        {
                            TargetID = heightTexture.LocalId,
                        },
                    });
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported prepared geometry asset batch type '{preparedGeometryBatch.GetType().Name}'.");
        }

        List<MaterialReferenceTarget> resolvedMaterialTargets = [];
        foreach (MaterialReferenceTarget materialTarget in materialTargets)
        {
            if (materialTarget.DedicatedMaterial is null)
            {
                resolvedMaterialTargets.Add(materialTarget);
                continue;
            }

            PendingBatchComponent dedicatedMaterial = await AddDedicatedMaterialOperationsAsync(
                batchBuilder,
                importClient,
                meshAssetSlot.LocalId,
                materialTarget.DedicatedMaterial,
                preparedTextureDataByKey,
                cancellationToken);
            resolvedMaterialTargets.Add(materialTarget with
            {
                TargetId = dedicatedMaterial.LocalId,
            });
        }

        Dictionary<string, Member> meshRendererMembers = new(StringComparer.Ordinal)
        {
            ["Mesh"] = new Reference
            {
                TargetID = geometryComponent.LocalId,
            },
            ["Materials"] = new SyncList
            {
                Elements = resolvedMaterialTargets
                    .Select(materialTarget => (Member)new Reference
                    {
                        TargetID = materialTarget.TargetId,
                    })
                    .ToList(),
            },
        };

        PendingBatchSlot presentationSlot = batchBuilder.AddSlot(
            objectSlots.LodSlot.SlotId,
            objectSlots.CityObjectSlotName,
            objectSlots.CityObjectLocalPosition,
            objectSlots.CityObjectRotation);
        batchBuilder.AddComponent(
            presentationSlot.LocalId,
            "[FrooxEngine]FrooxEngine.MeshRenderer",
            meshRendererMembers);
        batchBuilder.AddComponent(
            presentationSlot.LocalId,
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
                    TargetID = geometryComponent.LocalId,
                },
            });

        BatchResponse batchResponse = await client.RunDataModelOperationBatchAsync(batchBuilder.Operations, cancellationToken);
        CanonicalBatchEntityMap canonicalBatchEntityMap = CanonicalBatchEntityMap.Create(batchResponse);
        canonicalBatchEntityMap.ValidateAll(batchBuilder.PendingOperations);
        _ = canonicalBatchEntityMap.ResolveSlot(meshAssetSlot);
        _ = canonicalBatchEntityMap.ResolveComponent(geometryComponent);
        _ = canonicalBatchEntityMap.ResolveSlot(presentationSlot);
        if (heightMapAssetSlot is not null)
        {
            _ = canonicalBatchEntityMap.ResolveSlot(heightMapAssetSlot.Value);
        }
    }

    private async Task<PendingBatchComponent> AddDedicatedMaterialOperationsAsync(
        CityObjectBatchBuilder batchBuilder,
        IResoniteLinkClient importClient,
        string meshAssetSlotLocalId,
        ResoniteMaterialBinding material,
        IReadOnlyDictionary<TextureReferenceKey, ResoniteTextureImport> preparedTextureDataByKey,
        CancellationToken cancellationToken)
    {
        string materialSlotName = CreateMaterialSlotName(material, useCommonMaterialAssets: false);
        PendingBatchSlot materialSlot = batchBuilder.AddSlot(
            meshAssetSlotLocalId,
            materialSlotName,
            null,
            null);
        Dictionary<string, Member> materialMembers = ResoniteMaterialComponentBuilder.CreateMembers(material);

        if (material.TexturePath is not null
            && preparedTextureDataByKey.TryGetValue(
                ResoniteMaterialAssetManager.CreateTextureReferenceKey(material.TexturePath, material.TextureSourceKind),
                out ResoniteTextureImport? albedoTextureImport))
        {
            Uri albedoTextureUri = await ImportTextureAsync(importClient, albedoTextureImport, cancellationToken);
            PendingBatchComponent albedoTexture = batchBuilder.AddComponent(
                materialSlot.LocalId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                CreateTextureMembers(albedoTextureUri));
            materialMembers["AlbedoTexture"] = new Reference
            {
                TargetID = albedoTexture.LocalId,
            };
        }

        if (ResoniteMaterialComponentBuilder.TryGetBundledCompanionTextureSet(material, out BundledDefaultMaterialTextureSet? textureSet)
            && textureSet is not null)
        {
            if (textureSet.NormalPath is not null)
            {
                Uri normalTextureUri = await ImportTextureAsync(
                    importClient,
                    await ResoniteTextureImportFactory.CreateRawFromFileAsync(
                        textureSet.NormalPath,
                        ResoniteTextureColorProfiles.Linear,
                        cancellationToken),
                    cancellationToken);
                PendingBatchComponent normalTexture = batchBuilder.AddComponent(
                    materialSlot.LocalId,
                    "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    CreateTextureMembers(normalTextureUri));
                materialMembers["NormalMap"] = new Reference
                {
                    TargetID = normalTexture.LocalId,
                };
                materialMembers["NormalScale"] = new Field_float
                {
                    Value = DefaultNormalScale,
                };
            }

            if (textureSet.HeightPath is not null
                && material.Projection == ResoniteMaterialProjection.Uv)
            {
                Uri heightTextureUri = await ImportTextureAsync(
                    importClient,
                    await ResoniteTextureImportFactory.CreateRawFromFileAsync(
                        textureSet.HeightPath,
                        ResoniteTextureColorProfiles.Linear,
                        cancellationToken),
                    cancellationToken);
                PendingBatchComponent heightTexture = batchBuilder.AddComponent(
                    materialSlot.LocalId,
                    "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    CreateTextureMembers(heightTextureUri));
                materialMembers["HeightMap"] = new Reference
                {
                    TargetID = heightTexture.LocalId,
                };
                materialMembers["HeightScale"] = new Field_float
                {
                    Value = DefaultBundledHeightScale,
                };
            }

            if (textureSet.MetallicPath is not null)
            {
                Uri metallicTextureUri = await ImportTextureAsync(
                    importClient,
                    await ResoniteTextureImportFactory.CreateRawFromFileAsync(
                        textureSet.MetallicPath,
                        ResoniteTextureColorProfiles.Linear,
                        cancellationToken),
                    cancellationToken);
                PendingBatchComponent metallicTexture = batchBuilder.AddComponent(
                    materialSlot.LocalId,
                    "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    CreateTextureMembers(metallicTextureUri));
                materialMembers["MetallicMap"] = new Reference
                {
                    TargetID = metallicTexture.LocalId,
                };
                materialMembers["OcclusionMap"] = new Reference
                {
                    TargetID = metallicTexture.LocalId,
                };
            }

            if (textureSet.EmissionPath is not null)
            {
                Uri emissionTextureUri = await ImportTextureAsync(
                    importClient,
                    await ResoniteTextureImportFactory.CreateRawFromFileAsync(
                        textureSet.EmissionPath,
                        ResoniteTextureColorProfiles.Srgb,
                        cancellationToken),
                    cancellationToken);
                PendingBatchComponent emissionTexture = batchBuilder.AddComponent(
                    materialSlot.LocalId,
                    "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    CreateTextureMembers(emissionTextureUri));
                materialMembers["EmissiveMap"] = new Reference
                {
                    TargetID = emissionTexture.LocalId,
                };
                materialMembers["EmissiveColor"] = ResoniteMaterialComponentBuilder.CreateColorMember(
                    new ResoniteColor(1.0, 1.0, 1.0, 1.0));
            }
        }

        return batchBuilder.AddComponent(
            materialSlot.LocalId,
            ResoniteMaterialComponentBuilder.GetComponentType(material),
            materialMembers);
    }

    private static Dictionary<string, Member> CreateTextureMembers(Uri assetUri)
    {
        return new Dictionary<string, Member>(StringComparer.Ordinal)
        {
            ["URL"] = new Field_Uri
            {
                Value = assetUri,
            },
        };
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
        CreatedSlot createdSlot = await sharedSlotCache.GetOrCreateAsync(
            (parentId, slotName),
            ct => GetOrCreateSharedChildSlotCoreAsync(
                client,
                parentId,
                slotName,
                position,
                rotation,
                ct),
            cancellationToken);
        return createdSlot;
    }

    private static async Task<CreatedSlot> GetOrCreateSharedChildSlotCoreAsync(
        IResoniteLinkClient client,
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        CancellationToken cancellationToken)
    {
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
        ResoniteFloatQ? rotation,
        string? requestedSlotId = null,
        string? messageId = null)
    {
        return new AddSlot
        {
            MessageID = messageId,
            Data = new Slot
            {
                ID = requestedSlotId,
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

    private static AddComponent CreateAddComponentOperation(
        string containerSlotId,
        string componentType,
        IReadOnlyDictionary<string, Member> members,
        string? requestedComponentId = null,
        string? messageId = null)
    {
        return new AddComponent
        {
            MessageID = messageId,
            ContainerSlotId = containerSlotId,
            Data = new Component
            {
                ID = requestedComponentId,
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

    private readonly record struct PendingBatchSlot(
        string LocalId,
        string MessageId,
        string SlotName);

    private readonly record struct PendingBatchComponent(
        string LocalId,
        string MessageId,
        string ComponentType);

    private readonly record struct PendingBatchOperation(
        string MessageId,
        string Description);

    private readonly record struct MaterialReferenceTarget(
        string TargetId,
        ResoniteMaterialBinding? DedicatedMaterial)
    {
        public static MaterialReferenceTarget FromCanonical(string targetId)
        {
            return new MaterialReferenceTarget(targetId, null);
        }

        public static MaterialReferenceTarget FromDedicatedMaterial(ResoniteMaterialBinding material)
        {
            return new MaterialReferenceTarget(string.Empty, material);
        }
    }

    private sealed class CityObjectBatchBuilder
    {
        private readonly string batchScopeToken = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        private int nextEntityId;
        private int nextMessageId;

        public List<DataModelOperation> Operations { get; } = [];
        public List<PendingBatchOperation> PendingOperations { get; } = [];

        public PendingBatchSlot AddSlot(
            string parentId,
            string slotName,
            ResoniteFloat3? position,
            ResoniteFloatQ? rotation)
        {
            string localId = AllocateEntityId("local_slot");
            string messageId = AllocateMessageId();
            Operations.Add(CreateAddSlotOperation(parentId, slotName, position, rotation, localId, messageId));
            PendingOperations.Add(new PendingBatchOperation(messageId, $"slot '{slotName}'"));
            return new PendingBatchSlot(localId, messageId, slotName);
        }

        public PendingBatchComponent AddComponent(
            string containerSlotId,
            string componentType,
            IReadOnlyDictionary<string, Member> members)
        {
            string localId = AllocateEntityId("local_component");
            string messageId = AllocateMessageId();
            Operations.Add(CreateAddComponentOperation(containerSlotId, componentType, members, localId, messageId));
            PendingOperations.Add(new PendingBatchOperation(messageId, $"component '{componentType}'"));
            return new PendingBatchComponent(localId, messageId, componentType);
        }

        private string AllocateEntityId(string prefix)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{prefix}_{batchScopeToken}_{++nextEntityId}");
        }

        private string AllocateMessageId()
        {
            return string.Create(CultureInfo.InvariantCulture, $"batch_message_{batchScopeToken}_{++nextMessageId}");
        }
    }

    private sealed class CanonicalBatchEntityMap
    {
        private readonly Dictionary<string, Response> responsesByMessageId;

        private CanonicalBatchEntityMap(Dictionary<string, Response> responsesByMessageId)
        {
            this.responsesByMessageId = responsesByMessageId;
        }

        public static CanonicalBatchEntityMap Create(BatchResponse batchResponse)
        {
            ArgumentNullException.ThrowIfNull(batchResponse);
            return new CanonicalBatchEntityMap(
                (batchResponse.Responses ?? [])
                    .Where(static response => !string.IsNullOrWhiteSpace(response.SourceMessageID))
                    .ToDictionary(response => response.SourceMessageID, StringComparer.Ordinal));
        }

        public CreatedSlot ResolveSlot(PendingBatchSlot pendingSlot)
        {
            Response response = ResolveResponse(pendingSlot.MessageId);
            if (response is not NewEntityId newEntityId || string.IsNullOrWhiteSpace(newEntityId.EntityId))
            {
                throw new InvalidOperationException(
                    $"Batch response for slot '{pendingSlot.SlotName}' did not include a canonical slot ID.");
            }

            return new CreatedSlot(newEntityId.EntityId, pendingSlot.SlotName);
        }

        public CreatedComponent ResolveComponent(PendingBatchComponent pendingComponent)
        {
            Response response = ResolveResponse(pendingComponent.MessageId);
            if (response is not NewEntityId newEntityId || string.IsNullOrWhiteSpace(newEntityId.EntityId))
            {
                throw new InvalidOperationException(
                    $"Batch response for component '{pendingComponent.ComponentType}' did not include a canonical component ID.");
            }

            return new CreatedComponent(newEntityId.EntityId, pendingComponent.ComponentType);
        }

        public void ValidateAll(IReadOnlyList<PendingBatchOperation> pendingOperations)
        {
            ArgumentNullException.ThrowIfNull(pendingOperations);
            foreach (PendingBatchOperation pendingOperation in pendingOperations)
            {
                _ = ResolveResponse(
                    pendingOperation.MessageId,
                    $"validate {pendingOperation.Description}");
            }
        }

        private Response ResolveResponse(string messageId)
        {
            return ResolveResponse(messageId, $"resolve batch message '{messageId}'");
        }

        private Response ResolveResponse(string messageId, string operationName)
        {
            if (!responsesByMessageId.TryGetValue(messageId, out Response? response))
            {
                throw new InvalidOperationException($"Batch response did not include message '{messageId}'.");
            }

            ResoniteLinkClient.EnsureSuccess(response, operationName);
            return response;
        }
    }


    private sealed record ObjectSlotHierarchy(
        CreatedSlot MeshRootSlot,
        CreatedSlot AssetMeshRootSlot,
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
