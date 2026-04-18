using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Threading.Channels;

using ResoniteLink;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Application.Logging;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Targets.Resonite;

public sealed class ResoniteLinkSceneBuilder : ISceneImportTarget
{
    private const int MaxQueuedCityObjects = 4;
    private const long MaxInFlightCityObjectWorkingSetBytesPerLane = 256L * 1024L * 1024L;
    private const long MaxInFlightCityObjectWorkingSetBytesFloor = 512L * 1024L * 1024L;
    private const string RootSlotId = "Root";
    private const string DemPackageName = "dem";
    private const string HeightMapAssetSlotSuffix = "_heightmap";
    private const float DefaultNormalScale = 1.0f;
    private const float DefaultBundledHeightScale = 0.002f;
    private readonly Uri endpoint;
    private readonly int connectionCount;
    private readonly ResoniteLinkSendDiagnostics diagnostics;
    private readonly ITerrainTextureAssetGenerator terrainTextureAssetGenerator;
    private readonly ResoniteGeometryAssetAssembler geometryAssetAssembler;
#pragma warning disable CA1859
    private readonly ILiveSendClientSession clientSession;
#pragma warning restore CA1859
    private readonly Action<string>? progressReporter;
#pragma warning disable CA1859
    private readonly IResoniteSceneBootstrapCoordinator sceneBootstrapCoordinator;
#pragma warning restore CA1859
    private LiveSendExecutionRun? activeRun;
    private int executionClaimed;

    private LiveSendExecutionContext Context
        => activeRun?.Context
            ?? throw new ObjectDisposedException(nameof(ResoniteLinkSceneBuilder), "Live scene execution context is not initialized.");

    private LiveSendProgressState Progress
        => activeRun?.Progress
            ?? throw new ObjectDisposedException(nameof(ResoniteLinkSceneBuilder), "Live scene progress is not initialized.");

    private LiveSendMaterialState Materials
        => activeRun?.Materials
            ?? throw new ObjectDisposedException(nameof(ResoniteLinkSceneBuilder), "Live scene material state is not initialized.");

    private ResoniteScenePlacementSession Placement
        => activeRun?.Placement
            ?? throw new ObjectDisposedException(nameof(ResoniteLinkSceneBuilder), "Live scene placement state is not initialized.");

    private AsyncCompletedResultCache<TextureImportCacheKey, Uri> ImportedTextureUriCache
        => activeRun?.ImportedTextureUriCache
            ?? throw new ObjectDisposedException(nameof(ResoniteLinkSceneBuilder), "Texture import cache is not initialized.");

    private LiveSendExecutionRuntime processingRuntime
        => activeRun?.Runtime
            ?? throw new ObjectDisposedException(nameof(ResoniteLinkSceneBuilder), "Live send runtime is not initialized.");

    private ref int attemptedCityObjectCount => ref Progress.AttemptedCityObjectCount;

    private ref int processedCityObjectCount => ref Progress.ProcessedCityObjectCount;

    private ref int failedCityObjectCount => ref Progress.FailedCityObjectCount;

    private ref int firstQueuedCityObjectLogged => ref Progress.FirstQueuedCityObjectLogged;

    private ref int firstPreparedCityObjectLogged => ref Progress.FirstPreparedCityObjectLogged;

    private ref int firstBuiltCityObjectLogged => ref Progress.FirstBuiltCityObjectLogged;

    private ref int firstCityObjectPreparationStartedLogged => ref Progress.FirstCityObjectPreparationStartedLogged;

    private ref int firstCommonMaterialPrepLogged => ref Progress.FirstCommonMaterialPrepLogged;

    private ref int firstCityObjectStreamingStartedLogged => ref Progress.FirstCityObjectStreamingStartedLogged;

    private ref int firstCityObjectDequeuedLogged => ref Progress.FirstCityObjectDequeuedLogged;

    public ResoniteLinkSceneBuilder(Uri endpoint, Action<string>? progressReporter = null)
        : this(
            endpoint,
            4,
            ResoniteLinkSendDiagnostics.Disabled,
            CreateDefaultDependencies(
                endpoint,
                4,
                ResoniteLinkSendDiagnostics.Disabled,
                new TerrainTextureAssetGenerator(),
                progressReporter),
            enableMeshBake: true,
            progressReporter)
    {
    }

    public ResoniteLinkSceneBuilder(Uri endpoint, int connectionCount, Action<string>? progressReporter = null)
        : this(
            endpoint,
            connectionCount,
            ResoniteLinkSendDiagnostics.Disabled,
            CreateDefaultDependencies(
                endpoint,
                connectionCount,
                ResoniteLinkSendDiagnostics.Disabled,
                new TerrainTextureAssetGenerator(),
                progressReporter),
            enableMeshBake: true,
            progressReporter)
    {
    }

    internal ResoniteLinkSceneBuilder(
        Uri endpoint,
        int connectionCount,
        ResoniteLinkSendDiagnostics diagnostics,
        Action<string>? progressReporter = null)
        : this(
            endpoint,
            connectionCount,
            diagnostics,
            CreateDefaultDependencies(
                endpoint,
                connectionCount,
                diagnostics,
                new TerrainTextureAssetGenerator(),
                progressReporter),
            enableMeshBake: true,
            progressReporter)
    {
    }

    internal ResoniteLinkSceneBuilder(
        Uri endpoint,
        int connectionCount,
        ResoniteLinkSendDiagnostics diagnostics,
        bool enableMeshBake,
        Action<string>? progressReporter = null)
        : this(
            endpoint,
            connectionCount,
            diagnostics,
            CreateDefaultDependencies(
                endpoint,
                connectionCount,
                diagnostics,
                new TerrainTextureAssetGenerator(),
                progressReporter),
            enableMeshBake,
            progressReporter)
    {
    }

    internal ResoniteLinkSceneBuilder(
        Uri endpoint,
        int connectionCount,
        ResoniteLinkSendDiagnostics diagnostics,
        ResoniteLinkSceneBuilderDependencies dependencies,
        bool enableMeshBake = true,
        Action<string>? progressReporter = null)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(dependencies.ClientSession);
        ArgumentNullException.ThrowIfNull(dependencies.TerrainTextureAssetGenerator);

        this.endpoint = endpoint;
        this.connectionCount = connectionCount;
        this.diagnostics = diagnostics;
        this.terrainTextureAssetGenerator = dependencies.TerrainTextureAssetGenerator;
        MeshBakeEnabled = enableMeshBake;
        this.progressReporter = progressReporter;
        sceneBootstrapCoordinator = new ResoniteSceneBootstrapCoordinator(
            TryGetDatasetRootAsync,
            UpdateComponentAsync);
        geometryAssetAssembler = new ResoniteGeometryAssetAssembler(ReportProgress);
        clientSession = dependencies.ClientSession;
    }

    internal static ResoniteLinkSceneBuilderDependencies CreateDefaultDependencies(
        Uri endpoint,
        int connectionCount,
        ResoniteLinkSendDiagnostics diagnostics,
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator,
        Action<string>? progressReporter = null,
        Func<IResoniteLinkClient>? baseClientFactory = null)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetGenerator);

        return new ResoniteLinkSceneBuilderDependencies(
            ResoniteLinkTransportSessionFactory.Create(
                endpoint,
                connectionCount,
                diagnostics,
                progressReporter,
                baseClientFactory),
            terrainTextureAssetGenerator);
    }

    internal bool MeshBakeEnabled { get; }

    public async Task<SceneImportExecutionResult> ExecuteAsync(
        SceneImportExecutionPlan plan,
        IAsyncEnumerable<ImportedCityObject> cityObjects,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(cityObjects);
        if (Interlocked.Exchange(ref executionClaimed, 1) != 0)
        {
            throw new InvalidOperationException("A live scene build run is already active on this scene builder instance.");
        }
        bool completedSuccessfully = false;

        try
        {
            SceneBuildRequest request = plan.SceneBuildRequest;
            await BeginCoreAsync(
                CreateBootstrapInfo(request),
                request.WorkRoot,
                request.DatasetContentSource,
                CommonMaterialCatalog.CreateForPackages(request.Metadata.SourceDataset.PackageNames),
                plan.NormalizedRequest,
                SceneImportContractMapper.ToInternal(plan.SceneBuildRequest.Metadata).LocalOrigin,
                cancellationToken);

            await foreach (ImportedCityObject cityObject in cityObjects.WithCancellation(cancellationToken))
            {
                await ProcessCityObjectCoreAsync(SceneImportContractMapper.ToInternal(cityObject), cancellationToken);
            }

            IReadOnlyList<string> destinations = await CompleteCoreAsync(cancellationToken);
            completedSuccessfully = true;
            return new SceneImportExecutionResult(
                destinations,
                processedCityObjectCount,
                failedCityObjectCount);
        }
        finally
        {
            try
            {
                await ResetRunStateAsync(
                    disposeClients: false,
                    resetClients: !completedSuccessfully);
            }
            finally
            {
                Volatile.Write(ref executionClaimed, 0);
            }
        }
    }

    private async Task BeginCoreAsync(
        SceneBootstrapInfo bootstrapInfo,
        string workRoot,
        IPlateauDatasetContentSource datasetContentSource,
        IReadOnlyList<ResoniteMaterialBinding> commonMaterials,
        PlateauImportRequest normalizedRequest,
        ResoniteLocalOrigin requestLocalOrigin,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bootstrapInfo);
        ArgumentNullException.ThrowIfNull(datasetContentSource);
        ArgumentNullException.ThrowIfNull(commonMaterials);
        ArgumentNullException.ThrowIfNull(normalizedRequest);
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);

        string resolvedWorkRoot = Path.GetFullPath(workRoot);
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Initializing scene state for dataset '{bootstrapInfo.Dataset}' "
                + $"mesh '{bootstrapInfo.MeshCode}' at '{resolvedWorkRoot}'."));
        Stopwatch connectionStopwatch = Stopwatch.StartNew();
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Connecting ResoniteLink connection pool to {endpoint} "
                + $"with {connectionCount} available routed connection(s)."));
        await clientSession.EnsureConnectedAsync(normalizedRequest, cancellationToken);
        connectionStopwatch.Stop();
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"ResoniteLink connection pool ready in {connectionStopwatch.Elapsed.TotalSeconds:F2}s "
                + $"(dataset='{bootstrapInfo.Dataset}', mesh='{bootstrapInfo.MeshCode}')."));
        IResoniteLinkClient routedClient = GetRoutedClient();
        LiveSendProgressState progress = new();
        LiveSendMaterialState materials = new();
        ReportProgress(
            PlateauLog.Info(
                "live",
                "Reusing dataset content source provided by caller."));
        ResoniteTextureImageLoader textureImageLoader = new();
        ReportProgress(
            PlateauLog.Info("live", "Setting up mutable helpers (baker)."));
        ReportProgress(
            PlateauLog.Info(
                "live",
                "Starting setup slot bootstrap: dataset root, assets root, common assets root, location slot, and source-file root reference."));
        Stopwatch bootstrapStopwatch = Stopwatch.StartNew();
        ResoniteSceneBootstrapState bootstrapState = await sceneBootstrapCoordinator.BootstrapAsync(
            routedClient,
            bootstrapInfo,
            commonMaterials,
            cancellationToken);
        bootstrapStopwatch.Stop();
        ResoniteScenePlacementSession placement = new(
            bootstrapState.DatasetRootSlot,
            bootstrapState.DatasetAssetsRootSlot,
            requestLocalOrigin,
            CreateCityGmlSlotNamesByRelativePath(bootstrapInfo.SourceFiles),
            bootstrapState.SceneAnchor,
            CreateSlotAsync);
        placement.IndexBootstrapHierarchy(bootstrapState);
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Scene bootstrap complete in {bootstrapStopwatch.Elapsed.TotalSeconds:F2}s "
                + $"(dataset_root={bootstrapState.DatasetRootSlot.SlotName}, assets_root={bootstrapState.DatasetAssetsRootSlot.SlotName}, "
                + $"common_root={bootstrapState.CommonAssetsRootSlot.SlotName}, "
                + $"dataset_root_existed={bootstrapState.DatasetRootExisted}, "
                + $"location_slot='{bootstrapState.SceneAnchor.LocationSlotId}', "
                + $"anchor_mesh='{bootstrapState.SceneAnchor.MeshCode}', "
                + $"anchor_source_file_root='{bootstrapState.SceneAnchor.ReferenceSourceFileRootId ?? "<pending>"}')."));
        foreach ((string materialKey, CreatedMaterialAsset materialAsset) in bootstrapState.CommonMaterialAssetsByKey)
        {
            materials.CommonMaterialCreationTasks[materialKey] = Task.FromResult(materialAsset);
        }

        foreach (string family in bootstrapState.CommonMaterialFamilies)
        {
            materials.CommonMaterialFamilyWarmupTasks[family] = Task.CompletedTask;
        }

        if (bootstrapState.CommonMaterialAssetsByKey.Count > 0)
        {
            progress.FirstCommonMaterialPrepLogged = bootstrapState.CommonMaterialAssetsByKey.Count;
            ReportProgress(
                PlateauLog.Info(
                    "live",
                    $"Setup prepared {bootstrapState.CommonMaterialAssetsByKey.Count} common materials in bootstrap."));
        }
        else
        {
            ReportProgress(PlateauLog.Info("live", "No common materials needed setup creation during bootstrap."));
        }

        ReportProgress(
            PlateauLog.Info(
                "live",
                "Bootstrap fixed dataset license metadata/component before city-object streaming starts."));
        terrainTextureAssetGenerator.ResetUsageTracking();
        placement.DatasetLicenseComponentId = bootstrapState.DatasetLicenseComponentId;

        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Dataset metadata/license phase complete during bootstrap. "
                + $"Dataset root existed={bootstrapState.DatasetRootExisted}."));
        clientSession.BeginWorkerClientTracking();
        LiveSendRuntimePlan runtimePlan = CreateLiveSendRuntimePlan();
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Starting routed send workers (connection_pool={connectionCount})."));
        LiveSendExecutionRuntime runtime = new(runtimePlan, cancellationToken);
        progress.Reset();
        CompositeCityObjectBaker? cityObjectBaker = MeshBakeEnabled
            ? new CompositeCityObjectBaker(
                new Lod2AtlasCityObjectBaker(textureImageLoader),
                new FixedCellCityObjectMeshBaker())
            : null;
        LiveSendExecutionContext context = new(
            bootstrapInfo,
            bootstrapState.DatasetRootSlot,
            cityObjectBaker);
        activeRun = new LiveSendExecutionRun
        {
            Context = context,
            Progress = progress,
            Materials = materials,
            Placement = placement,
            ImportedTextureUriCache = new AsyncCompletedResultCache<TextureImportCacheKey, Uri>(),
            Runtime = runtime,
        };
        Stopwatch laneStartStopwatch = Stopwatch.StartNew();
        diagnostics.StartSendWindow(connectionCount);
        runtime.Start(CreateProcessingTasks(bootstrapInfo, runtime));
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Send lane tasks launched (connection budget={connectionCount}, "
                + $"queue_capacity_total={runtimePlan.QueueCapacity}, "
                + $"memory_budget_bytes={runtimePlan.MemoryBudgetBytes})."));
        laneStartStopwatch.Stop();
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Send workers ready against connection pool={connectionCount}."));
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Send lane startup phase complete in {laneStartStopwatch.Elapsed.TotalSeconds:F2}s."));
    }

    private Task<CreatedSlot?> TryGetDatasetRootAsync(
        IResoniteLinkClient client,
        string slotName,
        CancellationToken cancellationToken)
    {
        return TryGetDatasetRootCoreAsync(client, slotName, cancellationToken);
    }

    private static async Task<CreatedSlot?> TryGetDatasetRootCoreAsync(
        IResoniteLinkClient client,
        string slotName,
        CancellationToken cancellationToken)
    {
        ResoniteSceneSlotSnapshot snapshot = await ResoniteSceneSlotSnapshot.CreateAsync(
            client,
            RootSlotId,
            1,
            cancellationToken);
        ResoniteSceneChildLookupResult lookup = snapshot.GetUniqueChildLookupResult(slotName, RootSlotId);
        return lookup.State == ResoniteSceneChildLookupState.FoundWithId
            ? new CreatedSlot(lookup.SlotId!, slotName)
            : null;
    }

    private LiveSendRuntimePlan CreateLiveSendRuntimePlan()
    {
        return new LiveSendRuntimePlan(
            connectionCount,
            Math.Max(MaxQueuedCityObjects * connectionCount, connectionCount),
            Math.Max(
                MaxInFlightCityObjectWorkingSetBytesFloor,
                connectionCount * MaxInFlightCityObjectWorkingSetBytesPerLane));
    }

    private Task[] CreateProcessingTasks(
        SceneBootstrapInfo bootstrapInfo,
        LiveSendExecutionRuntime runtime)
    {
        Task[] tasks = new Task[connectionCount];
        for (int laneIndex = 0; laneIndex < connectionCount; laneIndex++)
        {
            int capturedLaneIndex = laneIndex;
            tasks[capturedLaneIndex] = ProcessQueuedCityObjectsOnLaneAsync(
                runtime.Reader,
                bootstrapInfo,
                capturedLaneIndex,
                runtime.ProcessingCancellationToken);
        }

        return tasks;
    }

    private async Task ProcessQueuedCityObjectsAsync(
        ChannelReader<QueuedCityObject> reader,
        int laneIndex,
        CancellationToken cancellationToken)
    {
        QueuedCityObject? currentCityObject = null;
        try
        {
            if (Interlocked.CompareExchange(ref firstCityObjectStreamingStartedLogged, 1, 0) == 0)
            {
                ReportProgress(
                    PlateauLog.Info(
                        "live",
                        $"City-object send pipeline is active and waiting for queue on lane {laneIndex + 1}/{connectionCount}."));
            }

            await foreach (QueuedCityObject queuedCityObject in reader.ReadAllAsync(cancellationToken))
            {
                currentCityObject = queuedCityObject;
                if (Interlocked.CompareExchange(ref firstCityObjectDequeuedLogged, 1, 0) == 0)
                {
                    ReportProgress(
                        PlateauLog.Info(
                            "live",
                            $"First city object dequeued on lane {laneIndex + 1}/{connectionCount} "
                            + $"after scene-start {GetSceneElapsedSeconds():F3}s: "
                            + $"{queuedCityObject.CityObject.DisplayName} "
                            + $"({queuedCityObject.CityObject.PackageName}/{queuedCityObject.CityObject.SlotKey})."));
                }

                await ProcessQueuedCityObjectAsync(queuedCityObject, cancellationToken);
                currentCityObject = null;
            }

            ReportProgress(
                PlateauLog.Info(
                    "live",
                    $"Send lane {laneIndex + 1}/{connectionCount} drained."));
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
            string cityObjectContext = currentCityObject is null
                ? string.Empty
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $" while processing '{currentCityObject.CityObject.DisplayName}' "
                    + $"({currentCityObject.CityObject.PackageName}/{currentCityObject.CityObject.SlotKey}) "
                    + $"mesh='{currentCityObject.CityObject.ActualMeshCode}' "
                    + $"sourceUnit='{currentCityObject.CityObject.SourceUnitKey ?? "<null>"}'");
            ReportProgress($"[live][error] Send lane {laneIndex + 1}/{connectionCount} failed{cityObjectContext}: {exception.Message}");
            throw;
        }
    }

    private async Task ProcessQueuedCityObjectsOnLaneAsync(
        ChannelReader<QueuedCityObject> reader,
        SceneBootstrapInfo bootstrapInfo,
        int laneIndex,
        CancellationToken cancellationToken)
    {
        Stopwatch laneClientStopwatch = Stopwatch.StartNew();
        if (laneIndex == 0)
        {
            ReportProgress(
                PlateauLog.Info(
                    "live",
                    $"Send worker {laneIndex + 1}/{connectionCount} is ready to consume from the routed connection pool."));
        }
        else
        {
            ReportProgress(
                PlateauLog.Info(
                    "live",
                    $"Preparing send worker {laneIndex + 1}/{connectionCount} "
                    + $"against routed connections to {endpoint} for dataset '{bootstrapInfo.Dataset}' mesh '{bootstrapInfo.MeshCode}'."));
        }
        laneClientStopwatch.Stop();
        try
        {
            if (laneIndex == 0)
            {
                ReportProgress(
                    PlateauLog.Info(
                        "live",
                        $"Send worker {laneIndex + 1}/{connectionCount} ready against routed connections "
                        + $"in {laneClientStopwatch.Elapsed.TotalSeconds:F2}s."));
            }
            else
            {
                ReportProgress(
                    PlateauLog.Info(
                        "live",
                        $"Send worker {laneIndex + 1}/{connectionCount} ready against routed connections "
                        + $"in {laneClientStopwatch.Elapsed.TotalSeconds:F2}s."));
            }
            await ProcessQueuedCityObjectsAsync(reader, laneIndex, cancellationToken);
        }
        catch (Exception exception)
        {
            TryMarkProcessingFailure(exception);
            CancelProcessing();
            throw;
        }
    }

    private async Task ProcessCityObjectCoreAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        CompositeCityObjectBaker? cityObjectBaker = Context.CityObjectBaker;
        if (cityObjectBaker is not null)
        {
            IReadOnlyList<ResoniteConstructionCityObject> queuedCityObjects = await cityObjectBaker.BufferAsync(
                cityObject,
                cancellationToken);
            if (queuedCityObjects.Count == 0)
            {
                return;
            }

            foreach (ResoniteConstructionCityObject queuedCityObject in queuedCityObjects)
            {
                await EnqueueCityObjectAsync(queuedCityObject, cancellationToken);
            }

            return;
        }

        await EnqueueCityObjectAsync(cityObject, cancellationToken);
    }

    private async Task<IReadOnlyList<string>> CompleteCoreAsync(CancellationToken cancellationToken = default)
    {
        LiveSendExecutionRuntime runtime = processingRuntime;
        IResoniteLinkClient routedClient = GetRoutedClient();
        LiveSendExecutionContext context = Context;
        CompositeCityObjectBaker? cityObjectBaker = context.CityObjectBaker;

        if (cityObjectBaker is not null)
        {
            (string Name, int InputCount, int OutputCount)[] pendingBakeSummaries = cityObjectBaker
                .GetBakeSummaries()
                .Where(static summary => summary.InputCount > 0)
                .ToArray();
            if (pendingBakeSummaries.Length > 0)
            {
                string summaryText = string.Join(
                    ", ",
                    pendingBakeSummaries.Select(static summary =>
                        $"{summary.Name}: input={summary.InputCount}, currentOutput={summary.OutputCount}"));
                ReportProgress(PlateauLog.Info("live", $"Starting buffered bake flush: {summaryText}."));
            }

            Stopwatch bakeFlushStopwatch = Stopwatch.StartNew();
            int bakedCityObjectCount = 0;
            List<Task> bakeEnqueueTasks = [];
            int maxInFlightBakeEnqueueTasks = Math.Max(4, connectionCount * 2);
            await cityObjectBaker.FlushAllAsync(
                async (bakedCityObject, callbackCancellationToken) =>
                {
                    _ = Interlocked.Increment(ref bakedCityObjectCount);
                    bakeEnqueueTasks.Add(EnqueueCityObjectAsync(bakedCityObject, callbackCancellationToken));
                    if (bakeEnqueueTasks.Count >= maxInFlightBakeEnqueueTasks)
                    {
                        await AwaitOneTaskSlotAsync(bakeEnqueueTasks, callbackCancellationToken);
                    }
                },
                cancellationToken);
            if (bakeEnqueueTasks.Count > 0)
            {
                await Task.WhenAll(bakeEnqueueTasks).WaitAsync(cancellationToken);
            }
            bakeFlushStopwatch.Stop();
            ReportProgress(
                PlateauLog.Info(
                    "live",
                    $"Buffered bake flush produced {bakedCityObjectCount} baked city objects "
                    + $"in {bakeFlushStopwatch.Elapsed.TotalSeconds:F3}s."));

            foreach ((string name, int inputCount, int outputCount) in cityObjectBaker.GetBakeSummaries().Where(static summary => summary.OutputCount > 0))
            {
                ReportProgress(
                    $"[live] {name} batched {inputCount} input city objects "
                    + $"into {outputCount} baked batch objects.");
            }
        }

        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Completing live send. Closing lane writers (attempted={attemptedCityObjectCount}, "
                + $"prepared={processedCityObjectCount}, failed={failedCityObjectCount})."));
        runtime.CompleteWriter();

        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Awaiting {runtime.ProcessingTaskCount} send lane task(s) to drain after queue close."));
        await runtime.AwaitCompletionAsync(cancellationToken);
        ReportProgress(PlateauLog.Info("live", "All send lanes drained and completion barrier passed."));
        Placement.DatasetLicenseComponentId = await sceneBootstrapCoordinator.ApplyDatasetLicenseAsync(
            routedClient,
            context.DatasetRootSlot.SlotId,
            terrainTextureAssetGenerator.ResolveDatasetLicense(context.BootstrapInfo.DatasetLicense),
            Placement.DatasetLicenseComponentId,
            allowUpdateExisting: true,
            cancellationToken);
        diagnostics.CompleteSendWindow();
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Completed {processedCityObjectCount} city objects "
                + $"(failed={failedCityObjectCount}, attempted={attemptedCityObjectCount})."));
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Send summary: attempted={attemptedCityObjectCount} sent={processedCityObjectCount} failed={failedCityObjectCount}."));

        return [$"{endpoint}#{Placement.SceneAnchor?.LocationSlotId ?? context.DatasetRootSlot.SlotId}"];
    }

    public async ValueTask DisposeAsync()
    {
        await ResetRunStateAsync(
            disposeClients: true,
            resetClients: false);
    }

    private async ValueTask ResetRunStateAsync(bool disposeClients, bool resetClients)
    {
        LiveSendExecutionRun? run = activeRun;
        if (run is not null)
        {
            await run.Runtime.DisposeAsync();
        }

        try
        {
            if (disposeClients)
            {
                clientSession.DisposeClients();
            }
            else if (resetClients)
            {
                await clientSession.ResetClientsAsync();
            }
        }
        finally
        {
            activeRun = null;
        }
    }

    private static Dictionary<string, string> CreateCityGmlSlotNamesByRelativePath(
        IReadOnlyList<string> relativeSourceFiles)
    {
        Dictionary<string, string> slotNamesByPath = new(StringComparer.Ordinal);
        Dictionary<string, List<string>> pathsByStem = new(StringComparer.Ordinal);

        foreach (string relativeSourceFile in relativeSourceFiles)
        {
            if (string.IsNullOrWhiteSpace(relativeSourceFile))
            {
                continue;
            }

            string fileStem = Path.GetFileNameWithoutExtension(relativeSourceFile);
            if (!pathsByStem.TryGetValue(fileStem, out List<string>? paths))
            {
                paths = [];
                pathsByStem.Add(fileStem, paths);
            }

            paths.Add(relativeSourceFile);
        }

        foreach ((string fileStem, List<string> paths) in pathsByStem)
        {
            paths.Sort(StringComparer.Ordinal);
            if (paths.Count == 1)
            {
                slotNamesByPath[paths[0]] = fileStem;
                continue;
            }

            foreach (string path in paths)
            {
                slotNamesByPath[path] = $"{fileStem}_{ComputeShortStableHash(path)}";
            }
        }

        return slotNamesByPath;
    }

    private static string ComputeShortStableHash(string value)
    {
        byte[] input = System.Text.Encoding.UTF8.GetBytes(value);
        byte[] hash = SHA256.HashData(input);
        return Convert.ToHexStringLower(hash.AsSpan(0, 4));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Live send should log and skip individual city object send failures while keeping the lane alive.")]
    private async Task ProcessQueuedCityObjectAsync(
        QueuedCityObject queuedCityObject,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref attemptedCityObjectCount);
        try
        {
            PreparedCityObject preparedCityObject = await AwaitWithSlowCityObjectWarningAsync(
                queuedCityObject.PreparationTask,
                cancellationToken);
            await BuildPreparedCityObjectAsync(queuedCityObject, preparedCityObject, cancellationToken);

            int processedCount = Interlocked.Increment(ref processedCityObjectCount);
            ReportProgress(
                $"[live] Sent city object {processedCount}: "
                + $"{preparedCityObject.CityObject.DisplayName} "
                + $"({preparedCityObject.CityObject.PackageName}/{preparedCityObject.CityObject.SlotKey})",
                PlateauLogLevel.Info);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (!IsRecoverableCityObjectSendFailure(exception))
            {
                throw;
            }

            int failedCount = Interlocked.Increment(ref failedCityObjectCount);
            ReportProgress(
                PlateauLog.Warning(
                    "live",
                    $"Skipping city object after send failure {failedCount}: "
                    + $"{queuedCityObject.CityObject.DisplayName} "
                    + $"({queuedCityObject.CityObject.PackageName}/{queuedCityObject.CityObject.SlotKey}). "
                    + $"Reason: {exception.Message}"));
        }
        finally
        {
            await queuedCityObject.MemoryLease.DisposeAsync();
        }
    }

    private static bool IsRecoverableCityObjectSendFailure(Exception exception)
    {
        return FindResoniteLinkOperationException(exception) is { OperationName: "ImportMesh" or "ImportTexture" or "GetSlot" or "GetComponent" };
    }

    private static Task<T> AwaitWithSlowCityObjectWarningAsync<T>(
        Task<T> operationTask,
        CancellationToken cancellationToken)
    {
        return operationTask.WaitAsync(cancellationToken);
    }

    private static ResoniteLinkOperationException? FindResoniteLinkOperationException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is ResoniteLinkOperationException operationException)
            {
                return operationException;
            }
        }

        return null;
    }

    private async Task EnqueueCityObjectAsync(
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken)
    {
        await AwaitProcessingTasksIfCompletedAsync();

        LiveSendExecutionRuntime runtime = processingRuntime
            ?? throw new ObjectDisposedException(nameof(ResoniteLinkSceneBuilder), "Live send runtime is not initialized.");
        long estimatedWorksetBytes = EstimateCityObjectWorkingSetBytes(cityObject);
        AsyncWeightedGate.Lease cityObjectMemoryLease = await runtime.AcquireCityObjectMemoryAsync(
            estimatedWorksetBytes,
            cancellationToken);
        Task<PreparedCityObject> preparationTask = CreatePreparationTask(cityObject, cancellationToken);
        Task<ResoniteScenePlacementSession.ObjectSlotHierarchy> objectHierarchyTask = CreateObjectHierarchyTask(cityObject, cancellationToken);
        if (Interlocked.CompareExchange(ref firstQueuedCityObjectLogged, 1, 0) == 0)
        {
            ReportProgress(
                PlateauLog.Info(
                    "live",
                    $"First city object queued after {GetSceneElapsedSeconds():F3}s: "
                    + $"{cityObject.DisplayName} ({cityObject.PackageName}/{cityObject.SlotKey}) "
                    + $"estimated_workset_bytes={estimatedWorksetBytes}."));
        }

        using CancellationTokenSource enqueueCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            runtime.ProcessingCancellationToken);
        try
        {
            await runtime.WriteAsync(
                new QueuedCityObject(cityObject, preparationTask, objectHierarchyTask, cityObjectMemoryLease),
                enqueueCancellation.Token);
        }
        catch (OperationCanceledException) when (runtime.IsCancellationRequested)
        {
            await cityObjectMemoryLease.DisposeAsync();
            await AwaitProcessingTasksIfCompletedAsync();
            throw;
        }
        catch
        {
            await cityObjectMemoryLease.DisposeAsync();
            _ = ObserveTaskFailureAsync(preparationTask);
            _ = ObserveTaskFailureAsync(objectHierarchyTask);
            throw;
        }

        await AwaitProcessingTasksIfCompletedAsync();
    }

    private static async Task AwaitOneTaskSlotAsync(
        List<Task> tasks,
        CancellationToken cancellationToken)
    {
        for (int index = tasks.Count - 1; index >= 0; index--)
        {
            if (!tasks[index].IsCompleted)
            {
                continue;
            }

            Task completedTask = tasks[index];
            tasks.RemoveAt(index);
            await completedTask.WaitAsync(cancellationToken);
            return;
        }

        Task finishedTask = await Task.WhenAny(tasks).WaitAsync(cancellationToken);
        tasks.Remove(finishedTask);
        await finishedTask.WaitAsync(cancellationToken);
    }

    private static long EstimateCityObjectWorkingSetBytes(ResoniteConstructionCityObject cityObject)
    {
        const long minimumWeightBytes = 256L * 1024L;
        const long textureReferenceWeightBytes = 4L * 1024L * 1024L;
        const long heightSampleWeightBytes = sizeof(double);
        const long hdrHeightTextureWeightBytes = 4L * sizeof(float);
        const long materialBindingWeightBytes = 512L;
        const long vertexWeightBytes = 64L;
        const long indexWeightBytes = sizeof(int);
        const long perSubmeshWeightBytes = 256L;

        long geometryWeightBytes = cityObject.Geometry switch
        {
            ResoniteTriangleMeshGeometry triangleMesh => EstimateTriangleMeshWorkingSetBytes(triangleMesh.Mesh),
            ResoniteHeightMapGridGeometry heightMap => checked(
                (heightMap.HeightSamples.Count * heightSampleWeightBytes)
                + ((long)heightMap.Width * heightMap.Height * hdrHeightTextureWeightBytes)),
            _ => minimumWeightBytes,
        };

        int distinctTextureCount = cityObject.Materials
            .Where(static material => material.TexturePayload is not null)
            .Select(static material => material.TexturePayload!.Identity)
            .Distinct()
            .Count();
        long materialWeightBytes = checked((cityObject.Materials.Count * materialBindingWeightBytes) + (distinctTextureCount * textureReferenceWeightBytes));
        return Math.Max(minimumWeightBytes, geometryWeightBytes + materialWeightBytes);

        static long EstimateTriangleMeshWorkingSetBytes(ResoniteImportedMesh mesh)
        {
            long vertexBytes = mesh.Vertices.Count * vertexWeightBytes;
            long indexBytes = mesh.Submeshes.Sum(static submesh => (long)submesh.TriangleVertexIndices.Count * indexWeightBytes);
            long submeshBytes = mesh.Submeshes.Count * perSubmeshWeightBytes;
            return checked(vertexBytes + indexBytes + submeshBytes);
        }
    }

    private void ReportProgress(string message)
    {
        ReportProgress(message, null);
    }

    private IResoniteLinkClient GetRoutedClient()
    {
        return clientSession.RoutedClient
            ?? throw new ObjectDisposedException(nameof(ILiveSendClientSession), "Routed ResoniteLink client is not connected.");
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
        if (Interlocked.CompareExchange(ref firstCityObjectPreparationStartedLogged, 1, 0) == 0)
        {
            ReportProgress(
                PlateauLog.Info(
                    "live",
                    $"City object preparation started after {GetSceneElapsedSeconds():F3}s: "
                    + $"{cityObject.DisplayName} ({cityObject.PackageName}/{cityObject.SlotKey}) "
                    + $"mesh='{cityObject.ActualMeshCode}'."));
        }

        CancellationToken processingCancellationToken = activeRun?.Runtime.ProcessingCancellationToken ?? CancellationToken.None;
        return PrepareCityObjectWithLinkedCancellationAsync(
            cityObject,
            callerCancellationToken,
            processingCancellationToken);
    }

    private Task<ResoniteScenePlacementSession.ObjectSlotHierarchy> CreateObjectHierarchyTask(
        ResoniteConstructionCityObject cityObject,
        CancellationToken callerCancellationToken)
    {
        CancellationToken processingCancellationToken = processingRuntime.ProcessingCancellationToken;
        return Placement.CreateObjectHierarchyTask(
            GetRoutedClient(),
            cityObject,
            processingCancellationToken,
            callerCancellationToken);
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
        if (cityObject.Geometry is ResoniteTriangleMeshGeometry triangleGeometry)
        {
            try
            {
                ValidateTriangleMeshBindings(cityObject, triangleGeometry.Mesh);
            }
            catch (Exception exception) when (exception is InvalidOperationException && exception is not ResoniteMeshValidationException)
            {
                throw new ResoniteMeshValidationException(
                    $"Triangle mesh '{cityObject.DisplayName}' failed sender-side validation. "
                    + $"{CreateTriangleMeshDiagnosticSummary(cityObject, triangleGeometry.Mesh)} "
                    + $"Reason: {exception.Message}",
                    exception);
            }

        }

        TerrainTextureOverlay[] distinctTerrainOverlays = cityObject.Materials
            .Where(static material => material.TerrainOverlay is not null)
            .Select(static material => material.TerrainOverlay!)
            .Distinct()
            .OrderBy(static overlay => overlay.PackageName, StringComparer.Ordinal)
            .ThenBy(static overlay => overlay.GeographicBounds.MinLatitude)
            .ThenBy(static overlay => overlay.GeographicBounds.MinLongitude)
            .ToArray();

        Task<PreparedTextureReference>[] terrainOverlayTexturePreparationTasks = distinctTerrainOverlays
            .Select(terrainTextureOverlay => PrepareTerrainOverlayTextureReferenceAsync(terrainTextureOverlay, cancellationToken))
            .ToArray();
        Task<PreparedTextureReference>[] texturePreparationTasks = [];

        Task<PreparedConstructionGeometry> geometryPreparationTask = cityObject.Geometry switch
        {
            ResoniteTriangleMeshGeometry triangleMesh => Task.Run<PreparedConstructionGeometry>(
                () => PrepareTriangleMeshGeometry(cityObject, triangleMesh.Mesh),
                cancellationToken),
            ResoniteHeightMapGridGeometry heightMap => Task.Run<PreparedConstructionGeometry>(
                () => new PreparedHeightMapGridGeometry(heightMap, PrepareHeightMapTexture(heightMap)),
                cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported geometry type '{cityObject.Geometry.GetType().Name}'."),
        };
        Stopwatch stopwatch = Stopwatch.StartNew();
        PreparedTextureReference[] preparedTextures = await Task.WhenAll(
            texturePreparationTasks
                .Concat(terrainOverlayTexturePreparationTasks)
                .Concat(cityObject.Materials
                    .Where(static material => material.TexturePayload is not null)
                    .Select(PrepareDirectMaterialTextureReferenceAsync)
                    .ToArray()));
        PreparedConstructionGeometry preparedGeometry = await geometryPreparationTask;
        stopwatch.Stop();
        diagnostics.RecordPrepare(cityObject.PackageName, stopwatch.Elapsed.TotalSeconds);

        if (Interlocked.CompareExchange(ref firstPreparedCityObjectLogged, 1, 0) == 0)
        {
            ReportProgress(
                PlateauLog.Info(
                    "live",
                    $"First city object prepared in {stopwatch.Elapsed.TotalSeconds:F3}s "
                    + $"after scene start {GetSceneElapsedSeconds():F3}s: "
                    + $"{cityObject.DisplayName} "
                    + $"(textures={preparedTextures.Length}, geometry={DescribePreparedGeometry(preparedGeometry)})."));
        }

        return new PreparedCityObject(
            cityObject,
            preparedGeometry,
            preparedTextures);
    }

    private async Task<PreparedTextureReference> PrepareTerrainOverlayTextureReferenceAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken)
    {
        ResoniteTextureImport terrainTextureImport = await terrainTextureAssetGenerator.EnsureTextureAsync(
            terrainTextureOverlay,
            cancellationToken);

        return new PreparedTextureReference(
            TextureIdentity: null,
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            TextureImport: terrainTextureImport,
            TerrainOverlay: terrainTextureOverlay);
    }

    private Task<PreparedTextureReference> PrepareDirectMaterialTextureReferenceAsync(
        ResoniteMaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(material.TexturePayload);

        return Task.FromResult(
            new PreparedTextureReference(
                TextureIdentity: material.TexturePayload.Identity,
                TextureSourceKind: material.TextureSourceKind,
                TextureImport: ResoniteTextureImportFactory.CreateRawFromPayload(material.TexturePayload),
                TerrainOverlay: null));
    }

    private async Task BuildPreparedCityObjectAsync(
        QueuedCityObject queuedCityObject,
        PreparedCityObject preparedCityObject,
        CancellationToken cancellationToken)
    {
        IResoniteLinkClient routedClient = GetRoutedClient();
        ResoniteConstructionCityObject cityObject = preparedCityObject.CityObject;
        using ResoniteLinkSendDiagnostics.CityObjectSendScope sendScope = diagnostics.BeginCityObjectSend(cityObject.PackageName);
        Stopwatch cityObjectStopwatch = Stopwatch.StartNew();
        ReportBuildStep(cityObject, "Creating object slot hierarchy.");
        Stopwatch slotHierarchyStopwatch = Stopwatch.StartNew();
        ResoniteScenePlacementSession.ObjectSlotHierarchy objectSlots = await AwaitWithSlowCityObjectWarningAsync(
            queuedCityObject.ObjectHierarchyTask,
            cancellationToken);
        slotHierarchyStopwatch.Stop();
        using CancellationTokenSource buildStepCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Dictionary<string, ResoniteTextureImport> preparedTextureDataByIdentity = CreatePreparedTextureDataByIdentity(preparedCityObject);
        Dictionary<TerrainTextureOverlay, ResoniteTextureImport> preparedTerrainTextureDataByOverlay = CreatePreparedTerrainTextureDataByOverlay(preparedCityObject);
        Stopwatch materialStopwatch = Stopwatch.StartNew();
        Stopwatch geometryStopwatch = new();
        Task<IReadOnlyList<PlannedMaterialAsset>> materialPlanningTask = PlanMaterialAssetsAsync(
            routedClient,
            cityObject,
            preparedTextureDataByIdentity,
            preparedTerrainTextureDataByOverlay,
            buildStepCancellation.Token);
        Task<PlannedGeometryAsset> geometryPlanningTask = PlanGeometryAssetAsync(
            routedClient,
            cityObject,
            preparedCityObject,
            buildStepCancellation.Token);
        IReadOnlyList<PlannedMaterialAsset> plannedMaterialAssets;
        PlannedGeometryAsset plannedGeometryAsset;
        try
        {
            plannedMaterialAssets = await materialPlanningTask;
            materialStopwatch.Stop();

            ReportBuildStep(cityObject, $"Preparing geometry assets ({DescribePreparedGeometry(preparedCityObject.Geometry)}).");
            geometryStopwatch.Start();
            plannedGeometryAsset = await geometryPlanningTask;
            geometryStopwatch.Stop();
        }
        catch
        {
            await buildStepCancellation.CancelAsync();
            await ObserveTaskFailuresAsync([materialPlanningTask, geometryPlanningTask]);
            throw;
        }

        PlannedSceneObjectEmission emissionPlan = new(
            plannedGeometryAsset,
            plannedMaterialAssets,
            new PlannedRenderer(
                plannedGeometryAsset.Identity,
                plannedMaterialAssets.Select(static asset => asset.Identity).ToArray()),
            new PlannedCollider(
                plannedGeometryAsset.Identity,
                cityObject.CollisionEnabled));
        PlannedBatchEmission batchEmission = CreatePlannedBatchEmission(objectSlots, emissionPlan);

        ReportBuildStep(cityObject, "Creating object-scoped DataModel batch.");
        Stopwatch batchStopwatch = Stopwatch.StartNew();
        await new PlannedBatchEmissionInterpreter(ReportProgress).ExecuteAsync(
            routedClient,
            cityObject,
            batchEmission,
            cancellationToken);
        batchStopwatch.Stop();

        ReportBuildStep(cityObject, "Live build completed.");
        cityObjectStopwatch.Stop();
        ReportProgress(
            PlateauLog.Debug(
                "live",
                $"City object '{cityObject.DisplayName}' phase timings: "
                + $"slot_hierarchy_s={slotHierarchyStopwatch.Elapsed.TotalSeconds:F3} "
                + $"geometry_assets_s={geometryStopwatch.Elapsed.TotalSeconds:F3} "
                + $"materials_s={materialStopwatch.Elapsed.TotalSeconds:F3} "
                + $"batch_s={batchStopwatch.Elapsed.TotalSeconds:F3} "
                + $"total_send_s={cityObjectStopwatch.Elapsed.TotalSeconds:F3}."));
        sendScope.MarkSent();
        if (Interlocked.CompareExchange(ref firstBuiltCityObjectLogged, 1, 0) == 0)
        {
            ReportProgress(
                $"[live] First city object built after {GetSceneElapsedSeconds():F3}s: "
                + $"{cityObject.DisplayName} ({cityObject.PackageName}/{cityObject.SlotKey})");
        }
    }

    private static Dictionary<string, ResoniteTextureImport> CreatePreparedTextureDataByIdentity(
        PreparedCityObject preparedCityObject)
    {
        return preparedCityObject.Textures
            .Where(static texture => !string.IsNullOrWhiteSpace(texture.TextureIdentity))
            .ToDictionary(
                static texture => texture.TextureIdentity!,
                static texture => texture.TextureImport,
                StringComparer.Ordinal);
    }

    private static Dictionary<TerrainTextureOverlay, ResoniteTextureImport> CreatePreparedTerrainTextureDataByOverlay(
        PreparedCityObject preparedCityObject)
    {
        return preparedCityObject.Textures
            .Where(static texture => texture.TerrainOverlay is not null)
            .ToDictionary(
                static texture => texture.TerrainOverlay!,
                static texture => texture.TextureImport);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Best-effort cleanup should observe and suppress orphaned import task failures after the primary send failure.")]
    private static async Task ObserveTaskFailureAsync(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
        }
    }

    private static Task ObserveTaskFailuresAsync(IEnumerable<Task> tasks)
    {
        return Task.WhenAll(tasks.Select(ObserveTaskFailureAsync));
    }

    private double GetSceneElapsedSeconds()
    {
        return activeRun?.Runtime.ElapsedTotalSeconds ?? 0.0;
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

    private static void ValidateTriangleMeshBindings(
        ResoniteConstructionCityObject cityObject,
        ResoniteImportedMesh mesh)
    {
        if (mesh.Submeshes.Count == 0)
        {
            throw new InvalidOperationException(
                $"Triangle mesh '{cityObject.DisplayName}' did not contain any submesh.");
        }

        if (cityObject.Materials.Count == 0)
        {
            throw new InvalidOperationException(
                $"Triangle mesh '{cityObject.DisplayName}' did not contain any material.");
        }

        Dictionary<int, ResoniteMeshSubmesh> submeshByIndex = mesh.Submeshes.ToDictionary(
            static submesh => submesh.Index,
            static submesh => submesh);
        if (submeshByIndex.Count != mesh.Submeshes.Count)
        {
            throw new InvalidOperationException(
                $"Triangle mesh '{cityObject.DisplayName}' contained duplicate submesh indices.");
        }

        Dictionary<int, string> materialKeyBySubmeshIndex = new();
        foreach (ResoniteMaterialBinding material in cityObject.Materials)
        {
            if (material.SubmeshIndices.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Triangle mesh '{cityObject.DisplayName}' material '{material.MaterialKey}' did not target any submesh.");
            }

            foreach (int submeshIndex in material.SubmeshIndices)
            {
                if (!submeshByIndex.ContainsKey(submeshIndex))
                {
                    throw new InvalidOperationException(
                        $"Triangle mesh '{cityObject.DisplayName}' material '{material.MaterialKey}' targeted missing submesh index {submeshIndex}.");
                }

                if (materialKeyBySubmeshIndex.TryGetValue(submeshIndex, out string? existingMaterialKey))
                {
                    throw new InvalidOperationException(
                        $"Triangle mesh '{cityObject.DisplayName}' assigned submesh index {submeshIndex} to both '{existingMaterialKey}' and '{material.MaterialKey}'.");
                }

                materialKeyBySubmeshIndex[submeshIndex] = material.MaterialKey;
            }
        }

        foreach (int submeshIndex in submeshByIndex.Keys.OrderBy(static index => index))
        {
            if (!materialKeyBySubmeshIndex.ContainsKey(submeshIndex))
            {
                throw new InvalidOperationException(
                    $"Triangle mesh '{cityObject.DisplayName}' left submesh index {submeshIndex} without a material assignment.");
            }
        }
    }

    private static PreparedTriangleMeshGeometry PrepareTriangleMeshGeometry(
        ResoniteConstructionCityObject cityObject,
        ResoniteImportedMesh mesh)
    {
        try
        {
            return new PreparedTriangleMeshGeometry(ResoniteMeshImportFactory.Create(mesh));
        }
        catch (Exception exception) when (exception is InvalidOperationException && exception is not ResoniteMeshValidationException)
        {
            throw new ResoniteMeshValidationException(
                $"Triangle mesh '{cityObject.DisplayName}' failed sender-side validation. "
                + $"{CreateTriangleMeshDiagnosticSummary(cityObject, mesh)} "
                + $"Reason: {exception.Message}",
                exception);
        }
    }

    private static string CreateTriangleMeshDiagnosticSummary(
        ResoniteConstructionCityObject cityObject,
        ResoniteImportedMesh mesh)
    {
        int[] submeshIndices = mesh.Submeshes
            .Select(static submesh => submesh.Index)
            .OrderBy(static index => index)
            .ToArray();
        string materialSummary = string.Join(
            ", ",
            cityObject.Materials.Select(static material =>
                $"{material.MaterialKey}[{string.Join("/", material.SubmeshIndices.OrderBy(static index => index))}]"));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"mesh_code={cityObject.ActualMeshCode}, vertices={mesh.Vertices.Count}, submeshes={mesh.Submeshes.Count}, "
            + $"submesh_indices=[{string.Join(", ", submeshIndices)}], materials={cityObject.Materials.Count}, "
            + $"material_bindings=[{materialSummary}]");
    }

    private async Task<IReadOnlyList<PlannedMaterialAsset>> PlanMaterialAssetsAsync(
        IResoniteLinkClient importClient,
        ResoniteConstructionCityObject cityObject,
        Dictionary<string, ResoniteTextureImport> preparedTextureDataByIdentity,
        Dictionary<TerrainTextureOverlay, ResoniteTextureImport> preparedTerrainTextureDataByOverlay,
        CancellationToken cancellationToken)
    {
        Task<PlannedMaterialAsset>[] plannedMaterialTasks = new Task<PlannedMaterialAsset>[cityObject.Materials.Count];
        for (int materialIndex = 0; materialIndex < cityObject.Materials.Count; materialIndex++)
        {
            ResoniteMaterialBinding material = cityObject.Materials[materialIndex];
            ReportBuildStep(
                cityObject,
                $"Creating material {materialIndex + 1}/{cityObject.Materials.Count} ({material.MaterialKey}).");
            if (material.AssetScope == ResoniteMaterialAssetScope.Common)
            {
                material = ResoniteSceneMaterialConventions.NormalizeCommonMaterialBinding(material);
                if (material.AssetScope != ResoniteMaterialAssetScope.Common)
                {
                    plannedMaterialTasks[materialIndex] = WrapDedicatedMaterialPlanningTask(
                        ResoniteMaterialPlanning.PlanDedicatedMaterialAssetAsync(
                            importClient,
                            material,
                            materialIndex,
                            cityObject.PackageName,
                            preparedTextureDataByIdentity,
                            preparedTerrainTextureDataByOverlay,
                            preserveDedicatedMaterialSlot: IsDemPackage(cityObject.PackageName),
                            cancellationToken));
                    continue;
                }

                string family = material.Family ?? BundledDefaultMaterialFamilies.Other;
                if (Materials.CommonMaterialFamilyWarmupTasks.TryGetValue(family, out Task? familyWarmupTask))
                {
                    await familyWarmupTask.WaitAsync(cancellationToken);
                }

                string materialKey = ResoniteSceneMaterialConventions.CreateCanonicalCommonMaterialKey(
                    family,
                    material.BundledVariantIndex ?? 0,
                    material.Projection,
                    material.TextureScale);
                if (!Materials.CommonMaterialCreationTasks.TryGetValue(materialKey, out Task<CreatedMaterialAsset>? materialCreationTask))
                {
                    throw new InvalidOperationException(
                        $"Bootstrap did not produce common material '{materialKey}'.");
                }

                CreatedMaterialAsset existingMaterialAsset = await materialCreationTask.WaitAsync(cancellationToken);
                plannedMaterialTasks[materialIndex] = Task.FromResult<PlannedMaterialAsset>(
                    new PlannedReusableMaterialAsset(
                        new MaterialIdentity(materialKey),
                        existingMaterialAsset.MaterialComponentId));
            }
            else
            {
                plannedMaterialTasks[materialIndex] = WrapDedicatedMaterialPlanningTask(
                    ResoniteMaterialPlanning.PlanDedicatedMaterialAssetAsync(
                        importClient,
                        material,
                        materialIndex,
                        cityObject.PackageName,
                        preparedTextureDataByIdentity,
                        preparedTerrainTextureDataByOverlay,
                        preserveDedicatedMaterialSlot: IsDemPackage(cityObject.PackageName),
                        cancellationToken));
            }
        }

        return await Task.WhenAll(plannedMaterialTasks);

        static async Task<PlannedMaterialAsset> WrapDedicatedMaterialPlanningTask(Task<PlannedDedicatedMaterialAsset> task)
        {
            return await task;
        }
    }

    private static bool IsDemPackage(string packageName)
    {
        return string.Equals(packageName, DemPackageName, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<PlannedGeometryAsset> PlanGeometryAssetAsync(
        IResoniteLinkClient importClient,
        ResoniteConstructionCityObject cityObject,
        PreparedCityObject preparedCityObject,
        CancellationToken cancellationToken)
    {
        return preparedCityObject.Geometry switch
        {
            PreparedTriangleMeshGeometry triangleMesh => CreatePlannedGeometryAsset(
                cityObject,
                await geometryAssetAssembler.PrepareTriangleMeshAsync(
                    importClient,
                    CreateMeshAssetSlotName(cityObject),
                    cityObject.DisplayName,
                    triangleMesh.MeshImport,
                    cancellationToken)),
            PreparedHeightMapGridGeometry heightMap => CreatePlannedGeometryAsset(
                cityObject,
                await geometryAssetAssembler.PrepareHeightMapGridAsync(
                    importClient,
                    CreateMeshAssetSlotName(cityObject),
                    CreateHeightMapAssetSlotName(cityObject),
                    cityObject.DisplayName,
                    heightMap.Geometry,
                    heightMap.HeightTextureImport,
                    cancellationToken)),
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

    private static PlannedGeometryAsset CreatePlannedGeometryAsset(
        ResoniteConstructionCityObject cityObject,
        PreparedGeometryAssetBatch preparedGeometryBatch)
    {
        GeometryIdentity identity = new(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{cityObject.PackageName}|{cityObject.SlotKey}|{preparedGeometryBatch.MeshAssetSlotName}"));

        return preparedGeometryBatch switch
        {
            PreparedTriangleMeshAssetBatch triangleMesh => new PlannedTriangleMeshGeometryAsset(
                identity,
                triangleMesh.MeshAssetSlotName,
                triangleMesh.MeshUri),
            PreparedHeightMapGridAssetBatch heightMap => new PlannedHeightMapGridGeometryAsset(
                identity,
                heightMap.MeshAssetSlotName,
                heightMap.HeightMapAssetSlotName,
                heightMap.Geometry,
                heightMap.HeightTextureUri),
            _ => throw new InvalidOperationException(
                $"Unsupported prepared geometry asset batch type '{preparedGeometryBatch.GetType().Name}'."),
        };
    }

    internal static PlannedBatchEmission CreatePlannedBatchEmission(
        ResoniteScenePlacementSession.ObjectSlotHierarchy objectSlots,
        PlannedSceneObjectEmission emissionPlan)
    {
        ArgumentNullException.ThrowIfNull(objectSlots);
        ArgumentNullException.ThrowIfNull(emissionPlan);

        List<PlannedBatchSlotEmission> slotEmissions = [];
        List<PlannedBatchComponentEmission> componentEmissions = [];
        List<BatchPlanEntityId> slotResolutionTargets = [];
        List<BatchPlanEntityId> componentResolutionTargets = [];

        BatchPlanEntityId meshAssetSlotId = CreateBatchPlanEntityId("mesh-asset-slot");
        slotEmissions.Add(new PlannedBatchSlotEmission(
            meshAssetSlotId,
            objectSlots.AssetLodSlot.SlotId,
            emissionPlan.GeometryAsset.MeshAssetSlotName,
            null,
            null));
        slotResolutionTargets.Add(meshAssetSlotId);

        BatchPlanEntityId geometryComponentId = CreateBatchPlanEntityId("geometry-component");
        switch (emissionPlan.GeometryAsset)
        {
            case PlannedTriangleMeshGeometryAsset triangleMesh:
                componentEmissions.Add(new PlannedBatchComponentEmission(
                    geometryComponentId,
                    meshAssetSlotId.Value,
                    "[FrooxEngine]FrooxEngine.StaticMesh",
                    new Dictionary<string, Member>(StringComparer.Ordinal)
                    {
                        ["URL"] = new Field_Uri
                        {
                            Value = triangleMesh.MeshUri,
                        },
                    }));
                break;
            case PlannedHeightMapGridGeometryAsset heightMap:
                BatchPlanEntityId heightMapAssetSlotId = CreateBatchPlanEntityId("heightmap-asset-slot");
                BatchPlanEntityId heightTextureComponentId = CreateBatchPlanEntityId("height-texture-component");
                slotEmissions.Add(new PlannedBatchSlotEmission(
                    heightMapAssetSlotId,
                    objectSlots.AssetLodSlot.SlotId,
                    heightMap.HeightMapAssetSlotName,
                    null,
                    null));
                slotResolutionTargets.Add(heightMapAssetSlotId);
                componentEmissions.Add(new PlannedBatchComponentEmission(
                    heightTextureComponentId,
                    heightMapAssetSlotId.Value,
                    "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    ResoniteGeometryAssetAssembler.CreateHeightMapTextureMembers(heightMap.HeightTextureUri)));
                double displacementMagnitude = Math.Max(heightMap.Geometry.MaxHeight - heightMap.Geometry.MinHeight, 0.0);
                componentEmissions.Add(new PlannedBatchComponentEmission(
                    geometryComponentId,
                    meshAssetSlotId.Value,
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
                            TargetID = heightTextureComponentId.Value,
                        },
                    }));
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported planned geometry asset type '{emissionPlan.GeometryAsset.GetType().Name}'.");
        }
        componentResolutionTargets.Add(geometryComponentId);

        Dictionary<MaterialIdentity, string> emittedMaterialTargets = new();
        foreach (PlannedMaterialAsset materialAsset in emissionPlan.MaterialAssets)
        {
            switch (materialAsset)
            {
                case PlannedReusableMaterialAsset reusableMaterial:
                    emittedMaterialTargets[reusableMaterial.Identity] = reusableMaterial.TargetId;
                    break;
                case PlannedDedicatedMaterialAsset dedicatedMaterial:
                    string emittedMaterialTarget = AddPlannedDedicatedMaterialEmissions(
                        slotEmissions,
                        componentEmissions,
                        meshAssetSlotId.Value,
                        dedicatedMaterial);
                    emittedMaterialTargets[dedicatedMaterial.Identity] = emittedMaterialTarget;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported planned material asset type '{materialAsset.GetType().Name}'.");
            }
        }

        BatchPlanEntityId presentationSlotId = CreateBatchPlanEntityId("presentation-slot");
        slotEmissions.Add(new PlannedBatchSlotEmission(
            presentationSlotId,
            objectSlots.LodSlot.SlotId,
            objectSlots.CityObjectSlotName,
            objectSlots.CityObjectLocalPosition,
            objectSlots.CityObjectRotation));
        slotResolutionTargets.Add(presentationSlotId);

        componentEmissions.Add(new PlannedBatchComponentEmission(
            CreateBatchPlanEntityId("mesh-renderer-component"),
            presentationSlotId.Value,
            "[FrooxEngine]FrooxEngine.MeshRenderer",
            new Dictionary<string, Member>(StringComparer.Ordinal)
            {
                ["Mesh"] = new Reference
                {
                    TargetID = geometryComponentId.Value,
                },
                ["Materials"] = new SyncList
                {
                    Elements = emissionPlan.Renderer.MaterialIdentities
                        .Select(materialIdentity => (Member)new Reference
                        {
                            TargetID = emittedMaterialTargets[materialIdentity],
                        })
                        .ToList(),
                },
            }));
        componentEmissions.Add(new PlannedBatchComponentEmission(
            CreateBatchPlanEntityId("mesh-collider-component"),
            presentationSlotId.Value,
            "[FrooxEngine]FrooxEngine.MeshCollider",
            new Dictionary<string, Member>(StringComparer.Ordinal)
            {
                ["Type"] = new Field_Enum
                {
                    Value = emissionPlan.Collider.CollisionEnabled ? "Static" : "NoCollision",
                },
                ["CharacterCollider"] = new Field_bool
                {
                    Value = emissionPlan.Collider.CollisionEnabled,
                },
                ["Mesh"] = new Reference
                {
                    TargetID = geometryComponentId.Value,
                },
            }));

        return new PlannedBatchEmission(
            slotEmissions,
            componentEmissions,
            slotResolutionTargets,
            componentResolutionTargets);
    }

    private static long EstimateBatchPayloadBytes(int operationCount)
    {
        return Math.Max(1L, operationCount) * 1024L;
    }

    private static string AddPlannedDedicatedMaterialEmissions(
        List<PlannedBatchSlotEmission> slotEmissions,
        List<PlannedBatchComponentEmission> componentEmissions,
        string meshAssetSlotTargetId,
        PlannedDedicatedMaterialAsset plannedMaterial)
    {
        ResoniteMaterialBinding material = plannedMaterial.Material;
        string materialContainerId = meshAssetSlotTargetId;
        if (plannedMaterial.PreserveDedicatedMaterialSlot)
        {
            BatchPlanEntityId materialSlotId = CreateBatchPlanEntityId($"material-slot:{plannedMaterial.Identity.Value}");
            string materialSlotName = ResoniteSceneMaterialConventions.CreateMaterialSlotName(material, useCommonMaterialAssets: false);
            slotEmissions.Add(new PlannedBatchSlotEmission(
                materialSlotId,
                meshAssetSlotTargetId,
                materialSlotName,
                null,
                null));
            materialContainerId = materialSlotId.Value;
        }

        Dictionary<string, Member> materialMembers = ResoniteMaterialComponentBuilder.CreateMembers(material);

        Uri? albedoTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(plannedMaterial.Textures, "albedo");
        if (albedoTextureUri is not null)
        {
            BatchPlanEntityId albedoTextureId = CreateBatchPlanEntityId($"material-texture:{plannedMaterial.Identity.Value}:albedo");
            componentEmissions.Add(new PlannedBatchComponentEmission(
                albedoTextureId,
                materialContainerId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ResoniteSceneMaterialConventions.CreateTextureMembers(albedoTextureUri)));
            materialMembers["AlbedoTexture"] = new Reference
            {
                TargetID = albedoTextureId.Value,
            };
        }

        Uri? normalTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(plannedMaterial.Textures, "normal");
        if (normalTextureUri is not null)
        {
            BatchPlanEntityId normalTextureId = CreateBatchPlanEntityId($"material-texture:{plannedMaterial.Identity.Value}:normal");
            componentEmissions.Add(new PlannedBatchComponentEmission(
                normalTextureId,
                materialContainerId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ResoniteSceneMaterialConventions.CreateTextureMembers(normalTextureUri)));
            materialMembers["NormalMap"] = new Reference
            {
                TargetID = normalTextureId.Value,
            };
            materialMembers["NormalScale"] = new Field_float
            {
                Value = DefaultNormalScale,
            };
        }

        Uri? heightTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(plannedMaterial.Textures, "height");
        if (heightTextureUri is not null)
        {
            BatchPlanEntityId heightTextureId = CreateBatchPlanEntityId($"material-texture:{plannedMaterial.Identity.Value}:height");
            componentEmissions.Add(new PlannedBatchComponentEmission(
                heightTextureId,
                materialContainerId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ResoniteSceneMaterialConventions.CreateTextureMembers(heightTextureUri)));
            materialMembers["HeightMap"] = new Reference
            {
                TargetID = heightTextureId.Value,
            };
            materialMembers["HeightScale"] = new Field_float
            {
                Value = DefaultBundledHeightScale,
            };
        }

        Uri? metallicTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(plannedMaterial.Textures, "metallic");
        if (metallicTextureUri is not null)
        {
            BatchPlanEntityId metallicTextureId = CreateBatchPlanEntityId($"material-texture:{plannedMaterial.Identity.Value}:metallic");
            componentEmissions.Add(new PlannedBatchComponentEmission(
                metallicTextureId,
                materialContainerId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ResoniteSceneMaterialConventions.CreateTextureMembers(metallicTextureUri)));
            materialMembers["MetallicMap"] = new Reference
            {
                TargetID = metallicTextureId.Value,
            };
            materialMembers["OcclusionMap"] = new Reference
            {
                TargetID = metallicTextureId.Value,
            };
        }

        Uri? emissionTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(plannedMaterial.Textures, "emission");
        if (emissionTextureUri is not null)
        {
            BatchPlanEntityId emissionTextureId = CreateBatchPlanEntityId($"material-texture:{plannedMaterial.Identity.Value}:emission");
            componentEmissions.Add(new PlannedBatchComponentEmission(
                emissionTextureId,
                materialContainerId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ResoniteSceneMaterialConventions.CreateTextureMembers(emissionTextureUri)));
            materialMembers["EmissiveMap"] = new Reference
            {
                TargetID = emissionTextureId.Value,
            };
            materialMembers["EmissiveColor"] = ResoniteMaterialComponentBuilder.CreateColorMember(
                new ResoniteColor(1.0, 1.0, 1.0, 1.0));
        }

        BatchPlanEntityId materialComponentId = CreateBatchPlanEntityId($"material-component:{plannedMaterial.Identity.Value}");
        componentEmissions.Add(new PlannedBatchComponentEmission(
            materialComponentId,
            materialContainerId,
            ResoniteMaterialComponentBuilder.GetComponentType(material),
            materialMembers));
        return materialComponentId.Value;
    }

    private Task<Uri?> ImportOptionalTextureAsync(
        IResoniteLinkClient importClient,
        ResoniteTextureImport textureImport,
        CancellationToken cancellationToken)
    {
        return ImportOptionalTextureCoreAsync(importClient, textureImport, cancellationToken);
    }

    private async Task<Uri?> ImportOptionalTextureCoreAsync(
        IResoniteLinkClient importClient,
        ResoniteTextureImport textureImport,
        CancellationToken cancellationToken)
    {
        return await ImportTextureAsync(importClient, textureImport, cancellationToken);
    }

    internal static Dictionary<string, Member> CreateTextureMembers(Uri assetUri)
    {
        return ResoniteSceneMaterialConventions.CreateTextureMembers(assetUri);
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

    private async Task AwaitProcessingTasksIfCompletedAsync()
    {
        if (activeRun is not null)
        {
            await activeRun.Runtime.AwaitIfAnyTaskCompletedAsync();
        }
    }

    private void TryMarkProcessingFailure(Exception exception)
    {
        activeRun?.Runtime.TryMarkFailure(exception);
    }

    private void CancelProcessing()
    {
        activeRun?.Runtime.Cancel();
    }

    private Task<CreatedSlot> CreateSlotAsync(
        IResoniteLinkClient client,
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        CancellationToken cancellationToken)
    {
        return CreateSlotCoreAsync(client, parentId, slotName, position, rotation, cancellationToken);
    }

    private static async Task<CreatedComponent> CreateComponentAsync(
        IResoniteLinkClient client,
        string containerSlotId,
        string componentType,
        IReadOnlyDictionary<string, Member> members,
        CancellationToken cancellationToken)
    {
        ResoniteBatchOperations.PendingBatchComponent pendingComponent = new(
            LocalId: $"single_component_{Guid.NewGuid():N}",
            MessageId: $"single_component_message_{Guid.NewGuid():N}",
            ComponentType: componentType);
        BatchResponse response = await client.RunDataModelOperationBatchAsync(
            [ResoniteBatchOperations.CreateAddComponentOperation(containerSlotId, componentType, members, pendingComponent.LocalId, pendingComponent.MessageId)],
            cancellationToken);
        return CanonicalBatchEntityMap.Create(response).ResolveComponent(pendingComponent);
    }

    private static Task UpdateComponentAsync(
        IResoniteLinkClient client,
        string componentId,
        IReadOnlyDictionary<string, Member> members,
        CancellationToken cancellationToken)
    {
        return client.UpdateComponentAsync(
            new UpdateComponent
            {
                Data = new Component
                {
                    ID = componentId,
                    Members = members.ToDictionary(
                        static pair => pair.Key,
                        static pair => pair.Value,
                        StringComparer.Ordinal),
                },
            },
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
        ResoniteBatchOperations.PendingBatchSlot pendingSlot = new(
            LocalId: $"single_slot_{Guid.NewGuid():N}",
            MessageId: $"single_slot_message_{Guid.NewGuid():N}",
            SlotName: slotName);
        BatchResponse response = await client.RunDataModelOperationBatchAsync(
            [ResoniteBatchOperations.CreateAddSlotOperation(parentId, slotName, position, rotation, requestedSlotId: pendingSlot.LocalId, messageId: pendingSlot.MessageId)],
            cancellationToken);
        return CanonicalBatchEntityMap.Create(response).ResolveSlot(pendingSlot);
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

        return await ImportedTextureUriCache.GetOrCreateAsync(
            cacheKey.Value,
            ct => client.ImportTextureAsync(textureImport, ct),
            cancellationToken);
    }

    private static TextureImportCacheKey? TryCreateTextureImportCacheKey(ResoniteTextureImport textureImport)
    {
        return textureImport switch
        {
            ResoniteRawTextureImport rawImport when rawImport.Identity is not null => new TextureImportCacheKey(
                "raw",
                rawImport.Identity,
                rawImport.ColorProfile),
            _ => null,
        };
    }

    private static string CreateMeshAssetSlotName(ResoniteConstructionCityObject cityObject)
    {
        return cityObject.DisplayName;
    }

    private static string CreateHeightMapAssetSlotName(ResoniteConstructionCityObject cityObject)
    {
        return string.Concat(CreateMeshAssetSlotName(cityObject), HeightMapAssetSlotSuffix);
    }

    private static BatchPlanEntityId CreateBatchPlanEntityId(string suffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suffix);
        return new BatchPlanEntityId($"plan:{suffix}");
    }

    private static SceneBootstrapInfo CreateBootstrapInfo(SceneBuildRequest request)
    {
        ResoniteConstructionMetadata metadata = SceneImportContractMapper.ToInternal(request.Metadata);
        return SceneBootstrapInfo.CreateFromMetadata(
            metadata,
            request.DatasetContentSource.SourcePath);
    }

    internal sealed record QueuedCityObject(
        ResoniteConstructionCityObject CityObject,
        Task<PreparedCityObject> PreparationTask,
        Task<ResoniteScenePlacementSession.ObjectSlotHierarchy> ObjectHierarchyTask,
        AsyncWeightedGate.Lease MemoryLease);

    internal abstract record PreparedConstructionGeometry;

    internal sealed record PreparedTriangleMeshGeometry(
        ImportMeshRawData MeshImport)
        : PreparedConstructionGeometry;

    internal sealed record PreparedHeightMapGridGeometry(
        ResoniteHeightMapGridGeometry Geometry,
        ResoniteRawHdrTextureImport HeightTextureImport)
        : PreparedConstructionGeometry;

    internal sealed record PreparedCityObject(
        ResoniteConstructionCityObject CityObject,
        PreparedConstructionGeometry Geometry,
        IReadOnlyList<PreparedTextureReference> Textures);

    internal sealed record PreparedTextureReference(
        string? TextureIdentity,
        ResoniteTextureSourceKind TextureSourceKind,
        ResoniteTextureImport TextureImport,
        TerrainTextureOverlay? TerrainOverlay = null);

    internal sealed record LiveSendRuntimePlan(
        int ConnectionCount,
        int QueueCapacity,
        long MemoryBudgetBytes);

}
