using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;

using ResoniteLink;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite.Execution;

namespace PlateauResoniteLink.Targets.Resonite;

public sealed class ResoniteLiveSceneImportTarget : ISceneImportTarget
{
    private const int MaxQueuedCityObjects = 4;
    private const long MaxInFlightCityObjectWorkingSetBytesPerLane = 256L * 1024L * 1024L;
    private const long MaxInFlightCityObjectWorkingSetBytesFloor = 512L * 1024L * 1024L;
    private const string DemPackageName = "dem";
    private const string HeightMapAssetSlotSuffix = "_heightmap";
    private readonly Uri endpoint;
    private readonly int connectionCount;
    private readonly ITerrainTextureAssetGenerator terrainTextureAssetGenerator;
    private readonly IResoniteDatasetLicenseWriter datasetLicenseWriter;
    private readonly IResoniteGeometryAssetAssembler geometryAssetAssembler;
    private readonly IResoniteMaterialPlanning materialPlanning;
    private readonly IResoniteBatchEmissionPlanner batchEmissionPlanner;
    private readonly IResoniteSceneBatchEmitter batchEmitter;
    private readonly IResoniteSlotCreator slotCreator;
    private readonly IResoniteBufferedCityObjectBakerFactory cityObjectBakerFactory;
#pragma warning disable CA1859
    private ILiveSendClientSession ClientSessionInternal { get; }
#pragma warning restore CA1859
    private readonly Action<string>? progressReporter;
#pragma warning disable CA1859
    private readonly IResoniteSceneBootstrapInterpreter sceneBootstrapInterpreter;
#pragma warning restore CA1859
    private int executionClaimed;

    internal ResoniteLiveSceneImportTarget(
        ResoniteLiveSceneImportTargetOptions options,
        ResoniteLiveSceneImportDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(dependencies.ClientSession);
        ArgumentNullException.ThrowIfNull(dependencies.TerrainTextureAssetGenerator);

        endpoint = options.Endpoint;
        connectionCount = options.ConnectionCount;
        MemoryProfile = options.MemoryProfile;
        Diagnostics = dependencies.Diagnostics;
        this.terrainTextureAssetGenerator = dependencies.TerrainTextureAssetGenerator;
        datasetLicenseWriter = dependencies.DatasetLicenseWriter;
        MeshBakeEnabled = options.EnableMeshBake;
        progressReporter = options.ProgressReporter;
        sceneBootstrapInterpreter = dependencies.SceneBootstrapInterpreter;
        geometryAssetAssembler = dependencies.GeometryAssetAssembler;
        materialPlanning = dependencies.MaterialPlanning;
        batchEmissionPlanner = dependencies.BatchEmissionPlanner;
        batchEmitter = dependencies.BatchEmitter;
        slotCreator = dependencies.SlotCreator;
        cityObjectBakerFactory = dependencies.CityObjectBakerFactory;
        ClientSessionInternal = dependencies.ClientSession;
    }

    internal bool MeshBakeEnabled { get; }

    internal ResoniteLinkSendDiagnostics Diagnostics { get; }

    internal ILiveSendClientSession ClientSession => ClientSessionInternal;

    internal PlateauImportMemoryProfile MemoryProfile { get; }

    public async Task<SceneImportExecutionResult> ExecuteAsync(
        SceneImportExecutionPlan plan,
        IAsyncEnumerable<ImportedCityObject> cityObjects,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(cityObjects);
        if (Interlocked.Exchange(ref executionClaimed, 1) != 0)
        {
            throw new InvalidOperationException("A live scene build run is already active on this live scene import target instance.");
        }
        bool completedSuccessfully = false;
        LiveSendRunState? state = null;

        try
        {
            SceneBuildRequest request = plan.SceneBuildRequest;
            state = await CreateRunStateAsync(
                CreateBootstrapInfo(request),
                request.WorkRoot,
                CommonMaterialCatalog.CreateForPackages(request.Metadata.SourceDataset.PackageNames),
                plan.NormalizedRequest,
                SceneImportContractMapper.ToInternal(plan.SceneBuildRequest.Metadata).LocalOrigin,
                cancellationToken);

            await foreach (ImportedCityObject cityObject in cityObjects.WithCancellation(cancellationToken))
            {
                await QueueCityObjectAsync(state, SceneImportContractMapper.ToInternal(cityObject), cancellationToken);
            }

            IReadOnlyList<string> destinations = await FinalizeRunAsync(state, cancellationToken);
            completedSuccessfully = true;
            return new SceneImportExecutionResult(
                destinations,
                state.Progress.ProcessedCityObjectCount,
                state.Progress.FailedCityObjectCount);
        }
        finally
        {
            try
            {
                await ReleaseRunResourcesAsync(
                    state,
                    disposeClients: false,
                    resetClients: !completedSuccessfully);
            }
            finally
            {
                Volatile.Write(ref executionClaimed, 0);
            }
        }
    }

    private async Task<LiveSendRunState> CreateRunStateAsync(
        SceneBootstrapInfo bootstrapInfo,
        string workRoot,
        IReadOnlyList<ResoniteMaterialBinding> commonMaterials,
        PlateauImportRequest normalizedRequest,
        ResoniteLocalOrigin requestLocalOrigin,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bootstrapInfo);
        ArgumentNullException.ThrowIfNull(commonMaterials);
        ArgumentNullException.ThrowIfNull(normalizedRequest);
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);

        string resolvedWorkRoot = Path.GetFullPath(workRoot);
        LiveSendRunPlan runPlan = CreateRunPlan(bootstrapInfo, resolvedWorkRoot, requestLocalOrigin);
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
        await ClientSessionInternal.EnsureConnectedAsync(normalizedRequest, cancellationToken);
        connectionStopwatch.Stop();
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"ResoniteLink connection pool ready in {connectionStopwatch.Elapsed.TotalSeconds:F2}s "
                + $"(dataset='{bootstrapInfo.Dataset}', mesh='{bootstrapInfo.MeshCode}')."));
        IResoniteLinkClient routedClient = GetRoutedClient();
        LiveSendProgressSink progress = new();
        CommonMaterialAssetCache materials = new();
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
        ResoniteSceneBootstrapState bootstrapState = await sceneBootstrapInterpreter.BootstrapAsync(
            routedClient,
            runPlan.BootstrapInfo,
            commonMaterials,
            cancellationToken);
        bootstrapStopwatch.Stop();
        ResoniteSharedSlotIndex placement = new(
            bootstrapState.DatasetRootSlot,
            bootstrapState.DatasetAssetsRootSlot,
            runPlan.RequestLocalOrigin,
            runPlan.SourceFileSlotNamesByRelativePath,
            bootstrapState.SceneAnchor,
            slotCreator.CreateAsync);
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
            materials.CommonMaterialCreationTasks.Remember(materialKey, materialAsset);
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
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Dataset metadata/license phase complete during bootstrap. "
                + $"Dataset root existed={bootstrapState.DatasetRootExisted}."));
        LiveSendQueuePlan runtimePlan = runPlan.Queue;
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Starting routed send workers (connection_pool={connectionCount})."));
        LiveSendExecutionRuntime runtime = new(runtimePlan, cancellationToken);
        progress.Reset();
        ResoniteImportBudgetProfile resourceBudget = runPlan.ResourceBudget;
        CompositeCityObjectBaker? cityObjectBaker = cityObjectBakerFactory.Create(
            runPlan.MeshBakeEnabled,
            textureImageLoader,
            resourceBudget);
        LiveSendRunContext context = new(
            runPlan,
            bootstrapState.DatasetRootSlot,
            bootstrapState.CommonAssetsRootSlot,
            cityObjectBaker);
        LiveSendRunState state = new()
        {
            Context = context,
            Progress = progress,
            Materials = materials,
            Placement = placement,
            ImportedTextureUriCache = new AsyncCompletedResultCache<TextureImportCacheKey, Uri>(),
            Runtime = runtime,
            GsiFallbackLicenseGate = new SemaphoreSlim(1, 1),
            ReportedDemSourceIdentities = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal),
        };
        Stopwatch laneStartStopwatch = Stopwatch.StartNew();
        Diagnostics.StartSendWindow(connectionCount);
        runtime.Start(CreateProcessingTasks(state, runtime));
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Send lane tasks launched (connection budget={connectionCount}, "
                + $"queue_capacity_total={runtimePlan.QueueCapacity}, "
                + $"memory_budget_bytes={runtimePlan.MemoryBudgetBytes}, "
                + $"memory_profile={resourceBudget.Name.ToString().ToLowerInvariant()}, "
                + $"runtime_vram_budget_bytes={resourceBudget.RuntimeVramBudgetBytes})."));
        laneStartStopwatch.Stop();
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Send workers ready against connection pool={connectionCount}."));
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Send lane startup phase complete in {laneStartStopwatch.Elapsed.TotalSeconds:F2}s."));
        return state;
    }

    private LiveSendRunPlan CreateRunPlan(
        SceneBootstrapInfo bootstrapInfo,
        string resolvedWorkRoot,
        ResoniteLocalOrigin requestLocalOrigin)
    {
        ResoniteImportBudgetProfile resourceBudget = ResoniteImportBudgetProfiles.ForProfile(MemoryProfile);
        return new LiveSendRunPlan(
            bootstrapInfo,
            resolvedWorkRoot,
            requestLocalOrigin,
            ResonitePlacementPolicy.CreateCityGmlSlotNamesByRelativePath(bootstrapInfo.SourceFiles),
            resourceBudget,
            new LiveSendQueuePlan(
                connectionCount,
                Math.Max(MaxQueuedCityObjects * connectionCount, connectionCount),
                Math.Max(resourceBudget.ImportWorkingSetBytes,
                    Math.Max(
                        MaxInFlightCityObjectWorkingSetBytesFloor,
                        connectionCount * MaxInFlightCityObjectWorkingSetBytesPerLane))),
            MeshBakeEnabled);
    }

    private Task[] CreateProcessingTasks(
        LiveSendRunState state,
        LiveSendExecutionRuntime runtime)
    {
        Task[] tasks = new Task[connectionCount];
        for (int laneIndex = 0; laneIndex < connectionCount; laneIndex++)
        {
            int capturedLaneIndex = laneIndex;
            tasks[capturedLaneIndex] = ProcessQueuedCityObjectsOnLaneAsync(
                state,
                runtime.Reader,
                capturedLaneIndex,
                runtime.ProcessingCancellationToken);
        }

        return tasks;
    }

    private async Task ProcessQueuedCityObjectsAsync(
        LiveSendRunState state,
        ChannelReader<QueuedCityObject> reader,
        int laneIndex,
        CancellationToken cancellationToken)
    {
        QueuedCityObject? currentCityObject = null;
        try
        {
            if (Interlocked.CompareExchange(ref state.Progress.FirstCityObjectStreamingStartedLogged, 1, 0) == 0)
            {
                ReportProgress(
                    PlateauLog.Info(
                        "live",
                        $"City-object send pipeline is active and waiting for queue on lane {laneIndex + 1}/{connectionCount}."));
            }

            await foreach (QueuedCityObject queuedCityObject in reader.ReadAllAsync(cancellationToken))
            {
                currentCityObject = queuedCityObject;
                if (Interlocked.CompareExchange(ref state.Progress.FirstCityObjectDequeuedLogged, 1, 0) == 0)
                {
                    ReportProgress(
                        PlateauLog.Info(
                            "live",
                            $"First city object dequeued on lane {laneIndex + 1}/{connectionCount} "
                            + $"after scene-start {GetSceneElapsedSeconds(state):F3}s: "
                            + $"{queuedCityObject.CityObject.DisplayName} "
                            + $"({queuedCityObject.CityObject.PackageName}/{queuedCityObject.CityObject.SlotKey})."));
                }

                await ProcessQueuedCityObjectAsync(state, queuedCityObject, cancellationToken);
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
            TryMarkProcessingFailure(state, exception);
            CancelProcessing(state);
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
        LiveSendRunState state,
        ChannelReader<QueuedCityObject> reader,
        int laneIndex,
        CancellationToken cancellationToken)
    {
        Stopwatch laneClientStopwatch = Stopwatch.StartNew();
        SceneBootstrapInfo bootstrapInfo = state.Context.Plan.BootstrapInfo;
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
            await ProcessQueuedCityObjectsAsync(state, reader, laneIndex, cancellationToken);
        }
        catch (Exception exception)
        {
            TryMarkProcessingFailure(state, exception);
            CancelProcessing(state);
            throw;
        }
    }

    private async Task QueueCityObjectAsync(
        LiveSendRunState state,
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        CompositeCityObjectBaker? cityObjectBaker = state.Context.CityObjectBaker;
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
                await EnqueueCityObjectAsync(state, queuedCityObject, cancellationToken);
            }

            return;
        }

        await EnqueueCityObjectAsync(state, cityObject, cancellationToken);
    }

    private async Task<IReadOnlyList<string>> FinalizeRunAsync(
        LiveSendRunState state,
        CancellationToken cancellationToken = default)
    {
        LiveSendExecutionRuntime runtime = state.Runtime;
        LiveSendRunContext context = state.Context;
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
                    bakeEnqueueTasks.Add(EnqueueCityObjectAsync(state, bakedCityObject, callbackCancellationToken));
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
                $"Completing live send. Closing lane writers (attempted={state.Progress.AttemptedCityObjectCount}, "
                + $"prepared={state.Progress.ProcessedCityObjectCount}, failed={state.Progress.FailedCityObjectCount})."));
        runtime.CompleteWriter();

        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Awaiting {runtime.ProcessingTaskCount} send lane task(s) to drain after queue close."));
        await runtime.AwaitCompletionAsync(cancellationToken);
        ReportProgress(PlateauLog.Info("live", "All send lanes drained and completion barrier passed."));
        Diagnostics.CompleteSendWindow();
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Completed {state.Progress.ProcessedCityObjectCount} city objects "
                + $"(failed={state.Progress.FailedCityObjectCount}, attempted={state.Progress.AttemptedCityObjectCount})."));
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Send summary: attempted={state.Progress.AttemptedCityObjectCount} sent={state.Progress.ProcessedCityObjectCount} failed={state.Progress.FailedCityObjectCount}."));

        return [$"{endpoint}#{state.Placement.SceneAnchor?.LocationSlotId ?? context.DatasetRootSlot.SlotId}"];
    }

    public async ValueTask DisposeAsync()
    {
        await ReleaseRunResourcesAsync(
            state: null,
            disposeClients: true,
            resetClients: false);
    }

    private async ValueTask ReleaseRunResourcesAsync(
        LiveSendRunState? state,
        bool disposeClients,
        bool resetClients)
    {
        if (state is not null)
        {
            await state.Runtime.DisposeAsync();
        }

        if (disposeClients)
        {
            ClientSessionInternal.DisposeClients();
        }
        else if (resetClients)
        {
            await ClientSessionInternal.ResetClientsAsync();
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Live send should log and skip individual city object send failures while keeping the lane alive.")]
    private async Task ProcessQueuedCityObjectAsync(
        LiveSendRunState state,
        QueuedCityObject queuedCityObject,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref state.Progress.AttemptedCityObjectCount);
        try
        {
            PreparedCityObject preparedCityObject = await AwaitWithSlowCityObjectWarningAsync(
                queuedCityObject.PreparationTask,
                cancellationToken);
            await BuildPreparedCityObjectAsync(state, queuedCityObject, preparedCityObject, cancellationToken);

            int processedCount = Interlocked.Increment(ref state.Progress.ProcessedCityObjectCount);
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

            int failedCount = Interlocked.Increment(ref state.Progress.FailedCityObjectCount);
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
        LiveSendRunState state,
        ResoniteConstructionCityObject cityObject,
        CancellationToken cancellationToken)
    {
        await AwaitProcessingTasksIfCompletedAsync(state);

        LiveSendExecutionRuntime runtime = state.Runtime;
        long estimatedWorksetBytes = EstimateCityObjectWorkingSetBytes(cityObject);
        AsyncWeightedGate.Lease cityObjectMemoryLease = await runtime.AcquireCityObjectMemoryAsync(
            estimatedWorksetBytes,
            cancellationToken);
        Task<PreparedCityObject> preparationTask = CreatePreparationTask(state, cityObject, cancellationToken);
        Task<ResoniteSharedSlotIndex.ObjectSlotHierarchy> objectHierarchyTask = CreateObjectHierarchyTask(state, cityObject, cancellationToken);
        if (Interlocked.CompareExchange(ref state.Progress.FirstQueuedCityObjectLogged, 1, 0) == 0)
        {
            ReportProgress(
                PlateauLog.Info(
                    "live",
                    $"First city object queued after {GetSceneElapsedSeconds(state):F3}s: "
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
            await AwaitProcessingTasksIfCompletedAsync(state);
            throw;
        }
        catch
        {
            await cityObjectMemoryLease.DisposeAsync();
            _ = ObserveTaskFailureAsync(preparationTask);
            _ = ObserveTaskFailureAsync(objectHierarchyTask);
            throw;
        }

        await AwaitProcessingTasksIfCompletedAsync(state);
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
        const long minimumWeightBytes = 16L * 1024L * 1024L;
        const long textureReferenceWeightBytes = 16L * 1024L * 1024L;
        const long heightSampleWeightBytes = sizeof(double);
        const long hdrHeightTextureWeightBytes = 4L * sizeof(float);
        const long materialBindingWeightBytes = 4096L;
        const long vertexWeightBytes = 256L;
        const long indexWeightBytes = 16L;
        const long perSubmeshWeightBytes = 4096L;
        const long triangleMeshExpansionFactor = 4L;
        const long heightMapExpansionFactor = 2L;

        long geometryWeightBytes = cityObject.Geometry switch
        {
            ResoniteTriangleMeshGeometry triangleMesh => checked(
                EstimateTriangleMeshWorkingSetBytes(triangleMesh.Mesh) * triangleMeshExpansionFactor),
            ResoniteHeightMapGridGeometry heightMap => checked(
                (heightMap.HeightSamples.Count * heightSampleWeightBytes)
                + ((long)heightMap.Width * heightMap.Height * hdrHeightTextureWeightBytes)
                * heightMapExpansionFactor),
            _ => minimumWeightBytes,
        };

        int distinctTextureCount = cityObject.Materials
            .Where(static material => material.TexturePayload is not null)
            .Select(static material => material.TexturePayload!.Identity)
            .Distinct()
            .Count();
        long terrainOverlayWeightBytes = cityObject.Materials
            .Where(static material => material.TerrainOverlay is not null)
            .Select(static material => material.TerrainOverlay!)
            .Distinct()
            .Sum(EstimateTerrainOverlayWorkingSetBytes);
        long materialWeightBytes = checked(
            (cityObject.Materials.Count * materialBindingWeightBytes)
            + (distinctTextureCount * textureReferenceWeightBytes)
            + terrainOverlayWeightBytes);
        return Math.Max(minimumWeightBytes, geometryWeightBytes + materialWeightBytes);

        static long EstimateTriangleMeshWorkingSetBytes(ResoniteImportedMesh mesh)
        {
            long vertexBytes = mesh.Vertices.Count * vertexWeightBytes;
            long indexBytes = mesh.Submeshes.Sum(static submesh => (long)submesh.TriangleVertexIndices.Count * indexWeightBytes);
            long submeshBytes = mesh.Submeshes.Count * perSubmeshWeightBytes;
            return checked(vertexBytes + indexBytes + submeshBytes);
        }

        static long EstimateTerrainOverlayWorkingSetBytes(TerrainTextureOverlay overlay)
        {
            const long rgbaBytesPerPixel = 4L;

            TerrainTextureTileSource? highestResolutionTileSource = overlay.EnumerateTileSources()
                .OrderByDescending(static source => source.ZoomLevel)
                .FirstOrDefault();
            if (highestResolutionTileSource is null)
            {
                return textureReferenceWeightBytes;
            }

            TerrainTextureLayoutPlan layout = TerrainTextureLayoutPlanner.Create(
                overlay.GeographicBounds,
                highestResolutionTileSource.ZoomLevel);
            int maxTextureEdge = RoundDownToPowerOfTwo(overlay.MaxTextureSize);
            int estimatedWidth = Math.Min(RoundUpToPowerOfTwo(layout.CropWidth), maxTextureEdge);
            int estimatedHeight = Math.Min(RoundUpToPowerOfTwo(layout.CropHeight), maxTextureEdge);
            return Math.Max(textureReferenceWeightBytes, checked((long)estimatedWidth * estimatedHeight * rgbaBytesPerPixel));
        }

        static int RoundUpToPowerOfTwo(int value)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);

            int rounded = 1;
            while (rounded < value)
            {
                rounded <<= 1;
            }

            return rounded;
        }

        static int RoundDownToPowerOfTwo(int value)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);

            int rounded = 1;
            while ((rounded << 1) > 0 && (rounded << 1) <= value)
            {
                rounded <<= 1;
            }

            return rounded;
        }
    }

    private void ReportProgress(string message)
    {
        ReportProgress(message, null);
    }

    private IResoniteLinkClient GetRoutedClient()
    {
        return ClientSessionInternal.RoutedClient
            ?? throw new ObjectDisposedException(nameof(ILiveSendClientSession), "Routed ResoniteLink client is not connected.");
    }

    private void ReportProgress(string message, PlateauLogLevel? defaultLevel)
    {
        PlateauLogLevel resolvedDefaultLevel = defaultLevel ?? PlateauLog.InferLegacyDefaultLevel(message);
        progressReporter?.Invoke(PlateauLog.NormalizeLegacyMessage(message, resolvedDefaultLevel));
    }

    private Task<PreparedCityObject> CreatePreparationTask(
        LiveSendRunState state,
        ResoniteConstructionCityObject cityObject,
        CancellationToken callerCancellationToken)
    {
        if (Interlocked.CompareExchange(ref state.Progress.FirstCityObjectPreparationStartedLogged, 1, 0) == 0)
        {
            ReportProgress(
                PlateauLog.Info(
                    "live",
                    $"City object preparation started after {GetSceneElapsedSeconds(state):F3}s: "
                    + $"{cityObject.DisplayName} ({cityObject.PackageName}/{cityObject.SlotKey}) "
                    + $"mesh='{cityObject.ActualMeshCode}'."));
        }

        CancellationToken processingCancellationToken = state.Runtime.ProcessingCancellationToken;
        return PrepareCityObjectWithLinkedCancellationAsync(
            state,
            cityObject,
            callerCancellationToken,
            processingCancellationToken);
    }

    private Task<ResoniteSharedSlotIndex.ObjectSlotHierarchy> CreateObjectHierarchyTask(
        LiveSendRunState state,
        ResoniteConstructionCityObject cityObject,
        CancellationToken callerCancellationToken)
    {
        CancellationToken processingCancellationToken = state.Runtime.ProcessingCancellationToken;
        return state.Placement.CreateObjectHierarchyTask(
            GetRoutedClient(),
            cityObject,
            processingCancellationToken,
            callerCancellationToken);
    }

    private async Task<PreparedCityObject> PrepareCityObjectWithLinkedCancellationAsync(
        LiveSendRunState state,
        ResoniteConstructionCityObject cityObject,
        CancellationToken callerCancellationToken,
        CancellationToken processingCancellationToken)
    {
        using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            callerCancellationToken,
            processingCancellationToken);
        return await PrepareCityObjectAsync(state, cityObject, linkedCancellation.Token);
    }

    private async Task<PreparedCityObject> PrepareCityObjectAsync(
        LiveSendRunState state,
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

        Task<PreparedTextureReference?>[] terrainOverlayTexturePreparationTasks = distinctTerrainOverlays
            .Select(terrainTextureOverlay => PrepareTerrainOverlayTextureReferenceAsync(state, terrainTextureOverlay, cancellationToken))
            .ToArray();
        Task<PreparedTextureReference?>[] texturePreparationTasks = [];

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
        PreparedTextureReference?[] preparedTextureResults = await Task.WhenAll(
            texturePreparationTasks
                .Concat(terrainOverlayTexturePreparationTasks)
                .Concat(cityObject.Materials
                    .Where(static material => material.TexturePayload is not null)
                    .Select(PrepareDirectMaterialTextureReferenceAsync)
                    .ToArray()));
        PreparedTextureReference[] preparedTextures = preparedTextureResults
            .OfType<PreparedTextureReference>()
            .ToArray();
        PreparedConstructionGeometry preparedGeometry = await geometryPreparationTask;
        stopwatch.Stop();
        Diagnostics.RecordPrepare(cityObject.PackageName, stopwatch.Elapsed.TotalSeconds);

        if (Interlocked.CompareExchange(ref state.Progress.FirstPreparedCityObjectLogged, 1, 0) == 0)
        {
            ReportProgress(
                PlateauLog.Info(
                    "live",
                    $"First city object prepared in {stopwatch.Elapsed.TotalSeconds:F3}s "
                    + $"after scene start {GetSceneElapsedSeconds(state):F3}s: "
                    + $"{cityObject.DisplayName} "
                    + $"(textures={preparedTextures.Length}, geometry={DescribePreparedGeometry(preparedGeometry)})."));
        }

        return new PreparedCityObject(
            cityObject,
            preparedGeometry,
            preparedTextures);
    }

    private async Task<PreparedTextureReference?> PrepareTerrainOverlayTextureReferenceAsync(
        LiveSendRunState state,
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken)
    {
        try
        {
            GeneratedTerrainTexture terrainTexture = await terrainTextureAssetGenerator.EnsureTextureAsync(
                terrainTextureOverlay,
                cancellationToken);
            TerrainTextureSource usedSource = terrainTexture.UsedSource ?? terrainTextureOverlay.PrimarySource;
            if (state.ReportedDemSourceIdentities.TryAdd(usedSource.IdentityKey, 0))
            {
                ReportProgress(
                    PlateauLog.Info(
                        "live",
                        $"Resolved DEM terrain texture source for package '{terrainTextureOverlay.PackageName}' "
                        + $"to {DescribeTerrainTextureSource(usedSource)}."));
            }

            if (IsGsiFallbackSource(usedSource))
            {
                await EnsureGsiFallbackLicenseAsync(state, cancellationToken);
            }

            return new PreparedTextureReference(
                TextureIdentity: null,
                TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                TextureImport: terrainTexture.TextureImport,
                TerrainOverlay: terrainTextureOverlay,
                GeneratedTerrainTexture: terrainTexture);
        }
        catch (HttpRequestException)
        {
            ReportProgress(
                PlateauLog.Warning(
                    "live",
                    $"Skipping terrain overlay texture for '{terrainTextureOverlay.SourceIdentityKey}' after texture generation failure."));
            return null;
        }
    }

    private async Task EnsureGsiFallbackLicenseAsync(
        LiveSendRunState state,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref state.GsiFallbackLicenseEnsured) != 0)
        {
            return;
        }

        await state.GsiFallbackLicenseGate.WaitAsync(cancellationToken);
        try
        {
            if (state.GsiFallbackLicenseEnsured != 0)
            {
                return;
            }

            await datasetLicenseWriter.EnsureGsiFallbackLicenseAsync(
                GetRoutedClient(),
                state.Context.DatasetRootSlot,
                cancellationToken);
            Volatile.Write(ref state.GsiFallbackLicenseEnsured, 1);
        }
        finally
        {
            state.GsiFallbackLicenseGate.Release();
        }
    }

    private static bool IsGsiFallbackSource(TerrainTextureSource source)
    {
        return source is TerrainTextureTileSource tileSource
            && string.Equals(
                tileSource.UrlTemplate,
                LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackUrlTemplate,
                StringComparison.Ordinal)
            && tileSource.ZoomLevel == LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackZoomLevel;
    }

    private static string DescribeTerrainTextureSource(TerrainTextureSource source)
    {
        return source switch
        {
            TerrainTextureGeoReferencedRasterSource rasterSource => string.Create(
                CultureInfo.InvariantCulture,
                $"GeoTIFF(path='{Path.GetFileName(rasterSource.SourcePath)}', crs='{rasterSource.Metadata?.CoordinateSystemIdentifier ?? "unknown"}')"),
            TerrainTextureTileSource tileSource when IsGsiFallbackSource(tileSource) => string.Create(
                CultureInfo.InvariantCulture,
                $"GSI seamless photo tile(z={tileSource.ZoomLevel})"),
            TerrainTextureTileSource tileSource => string.Create(
                CultureInfo.InvariantCulture,
                $"PLATEAU-Ortho tile(z={tileSource.ZoomLevel})"),
            _ => source.GetType().Name,
        };
    }

    private Task<PreparedTextureReference?> PrepareDirectMaterialTextureReferenceAsync(
        ResoniteMaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(material.TexturePayload);

        return Task.FromResult<PreparedTextureReference?>(
            new PreparedTextureReference(
                TextureIdentity: material.TexturePayload.Identity,
                TextureSourceKind: material.TextureSourceKind,
                TextureImport: ResoniteTextureImportFactory.CreateRawFromPayload(material.TexturePayload),
                TerrainOverlay: null));
    }

    private async Task BuildPreparedCityObjectAsync(
        LiveSendRunState state,
        QueuedCityObject queuedCityObject,
        PreparedCityObject preparedCityObject,
        CancellationToken cancellationToken)
    {
        IResoniteLinkClient routedClient = GetRoutedClient();
        ResoniteConstructionCityObject cityObject = preparedCityObject.CityObject;
        using ResoniteLinkSendDiagnostics.CityObjectSendScope sendScope = Diagnostics.BeginCityObjectSend(cityObject.PackageName);
        Stopwatch cityObjectStopwatch = Stopwatch.StartNew();
        ReportBuildStep(cityObject, "Creating object slot hierarchy.");
        Stopwatch slotHierarchyStopwatch = Stopwatch.StartNew();
        ResoniteSharedSlotIndex.ObjectSlotHierarchy objectSlots = await AwaitWithSlowCityObjectWarningAsync(
            queuedCityObject.ObjectHierarchyTask,
            cancellationToken);
        slotHierarchyStopwatch.Stop();
        using CancellationTokenSource buildStepCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Dictionary<string, ResoniteTextureImport> preparedTextureDataByIdentity = CreatePreparedTextureDataByIdentity(preparedCityObject);
        Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay = CreatePreparedTerrainTextureDataByOverlay(preparedCityObject);
        Stopwatch materialStopwatch = Stopwatch.StartNew();
        Stopwatch geometryStopwatch = new();
        Task<PlannedSceneMaterialPlan> materialPlanningTask = PlanSceneMaterialPlanAsync(
            state,
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
        PlannedSceneMaterialPlan plannedMaterials;
        PlannedGeometryAsset plannedGeometryAsset;
        try
        {
            plannedMaterials = await materialPlanningTask;
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
            plannedMaterials.MaterialAssets,
            new PlannedRenderer(
                plannedGeometryAsset.Identity,
                plannedMaterials.RendererMaterialBindings),
            new PlannedCollider(
                plannedGeometryAsset.Identity,
                cityObject.CollisionEnabled));
        PlannedBatchEmission batchEmission = batchEmissionPlanner.Create(objectSlots, emissionPlan);

        ReportBuildStep(cityObject, "Creating object-scoped DataModel batch.");
        Stopwatch batchStopwatch = Stopwatch.StartNew();
        await batchEmitter.ExecuteAsync(
            routedClient,
            cityObject,
            batchEmission,
            progressReporter,
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
        if (Interlocked.CompareExchange(ref state.Progress.FirstBuiltCityObjectLogged, 1, 0) == 0)
        {
            ReportProgress(
                $"[live] First city object built after {GetSceneElapsedSeconds(state):F3}s: "
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

    private static Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> CreatePreparedTerrainTextureDataByOverlay(
        PreparedCityObject preparedCityObject)
    {
        return preparedCityObject.Textures
            .Where(static texture => texture is { TerrainOverlay: not null, GeneratedTerrainTexture: not null })
            .ToDictionary(
                static texture => texture.TerrainOverlay!,
                static texture => texture.GeneratedTerrainTexture!);
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

    private static double GetSceneElapsedSeconds(LiveSendRunState state)
    {
        return state.Runtime.ElapsedTotalSeconds;
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

    private async Task<PlannedSceneMaterialPlan> PlanSceneMaterialPlanAsync(
        LiveSendRunState state,
        IResoniteLinkClient importClient,
        ResoniteConstructionCityObject cityObject,
        Dictionary<string, ResoniteTextureImport> preparedTextureDataByIdentity,
        Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay,
        CancellationToken cancellationToken)
    {
        Task<(PlannedMaterialAsset MaterialAsset, PlannedRendererMaterialBinding RendererBinding)>[] materialPlanTasks
            = new Task<(PlannedMaterialAsset MaterialAsset, PlannedRendererMaterialBinding RendererBinding)>[cityObject.Materials.Count];
        for (int materialIndex = 0; materialIndex < cityObject.Materials.Count; materialIndex++)
        {
            ResoniteMaterialBinding material = ResoniteMaterialPlanning.ResolveTerrainTextureCanvasMaterial(
                cityObject.Materials[materialIndex],
                preparedTerrainTextureDataByOverlay);
            ReportBuildStep(
                cityObject,
                $"Creating material {materialIndex + 1}/{cityObject.Materials.Count} ({material.MaterialKey}).");
            if (TryCreateSharedCommonRendererMaterialPlanTask(
                    state,
                    importClient,
                    material,
                    preparedTextureDataByIdentity,
                    preparedTerrainTextureDataByOverlay,
                    cancellationToken,
                    out Task<(PlannedMaterialAsset MaterialAsset, PlannedRendererMaterialBinding RendererBinding)>? sharedCommonPlanTask))
            {
                materialPlanTasks[materialIndex] = sharedCommonPlanTask
                    ?? throw new InvalidOperationException("Shared common renderer material planning task was not created.");
                continue;
            }

            materialPlanTasks[materialIndex] = PlanDedicatedRendererMaterialAsync(
                importClient,
                material,
                materialIndex,
                cityObject.PackageName,
                preparedTextureDataByIdentity,
                preparedTerrainTextureDataByOverlay,
                preserveDedicatedMaterialSlot: IsDemPackage(cityObject.PackageName),
                cancellationToken);
        }

        (PlannedMaterialAsset MaterialAsset, PlannedRendererMaterialBinding RendererBinding)[] materialPlans = await Task.WhenAll(materialPlanTasks);
        return new PlannedSceneMaterialPlan(
            materialPlans.Select(static plan => plan.MaterialAsset).ToArray(),
            materialPlans.Select(static plan => plan.RendererBinding).ToArray());

        bool TryCreateSharedCommonRendererMaterialPlanTask(
            LiveSendRunState runState,
            IResoniteLinkClient client,
            ResoniteMaterialBinding sourceMaterial,
            IReadOnlyDictionary<string, ResoniteTextureImport> preparedTexturesByIdentity,
            IReadOnlyDictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTexturesByOverlay,
            CancellationToken ct,
            out Task<(PlannedMaterialAsset MaterialAsset, PlannedRendererMaterialBinding RendererBinding)>? sharedPlanTask)
        {
            sharedPlanTask = null;
            if (!ResoniteSceneMaterialConventions.TryNormalizeSharedMaterialBinding(
                    sourceMaterial,
                    out ResoniteMaterialBinding normalizedSharedMaterial,
                    out string familySlotName))
            {
                return false;
            }

            string materialKey = normalizedSharedMaterial.MaterialKey;
            sharedPlanTask = PlanSharedCommonRendererMaterialAsync(
                runState,
                client,
                sourceMaterial,
                normalizedSharedMaterial,
                familySlotName,
                materialKey,
                preparedTexturesByIdentity,
                preparedTexturesByOverlay,
                ct);
            return true;
        }

        async Task<(PlannedMaterialAsset MaterialAsset, PlannedRendererMaterialBinding RendererBinding)> PlanSharedCommonRendererMaterialAsync(
            LiveSendRunState runState,
            IResoniteLinkClient client,
            ResoniteMaterialBinding sourceMaterial,
            ResoniteMaterialBinding normalizedSharedMaterial,
            string familySlotName,
            string materialKey,
            IReadOnlyDictionary<string, ResoniteTextureImport> preparedTexturesByIdentity,
            IReadOnlyDictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTexturesByOverlay,
            CancellationToken ct)
        {
            if (runState.Materials.CommonMaterialFamilyWarmupTasks.TryGetValue(familySlotName, out Task? familyWarmupTask))
            {
                await familyWarmupTask.WaitAsync(ct);
            }

            CreatedMaterialAsset existingMaterialAsset = await runState.Materials.CommonMaterialCreationTasks.GetOrCreateAsync(
                materialKey,
                () => new LazySharedMaterialTaskFactory(
                    runState,
                    client,
                    materialPlanning,
                    normalizedSharedMaterial,
                    familySlotName,
                    ResoniteMaterialPlanning.CreateComponentAsync).CreateLazySharedMaterialTask(materialKey),
                ct);
            PlannedReusableMaterialAsset sharedMaterialAsset = new(
                new MaterialIdentity(materialKey),
                existingMaterialAsset.MaterialComponentId);
            PlannedTextureAsset? mainTextureOverride = await ResoniteMaterialPlanning.PlanMainTextureOverrideAsync(
                client,
                sourceMaterial,
                preparedTexturesByIdentity,
                preparedTexturesByOverlay,
                ct);
            PlannedRendererMaterialBinding rendererBinding = mainTextureOverride is null
                ? new PlannedDirectRendererMaterialBinding(sharedMaterialAsset.Identity)
                : new PlannedMainTextureOverrideRendererMaterialBinding(sharedMaterialAsset.Identity, mainTextureOverride);
            return (sharedMaterialAsset, rendererBinding);
        }

        async Task<(PlannedMaterialAsset MaterialAsset, PlannedRendererMaterialBinding RendererBinding)> PlanDedicatedRendererMaterialAsync(
            IResoniteLinkClient client,
            ResoniteMaterialBinding sourceMaterial,
            int materialIndex,
            string packageName,
            IReadOnlyDictionary<string, ResoniteTextureImport> preparedTexturesByIdentity,
            IReadOnlyDictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTexturesByOverlay,
            bool preserveDedicatedMaterialSlot,
            CancellationToken ct)
        {
            PlannedDedicatedMaterialAsset plannedMaterial = await materialPlanning.PlanDedicatedMaterialAssetAsync(
                client,
                sourceMaterial,
                materialIndex,
                packageName,
                preparedTexturesByIdentity,
                preparedTexturesByOverlay,
                preserveDedicatedMaterialSlot,
                ct);
            return (plannedMaterial, new PlannedDirectRendererMaterialBinding(plannedMaterial.Identity));
        }
    }

    private readonly record struct LazySharedMaterialTaskFactory(
        LiveSendRunState RunState,
        IResoniteLinkClient Client,
        IResoniteMaterialPlanning MaterialPlanning,
        ResoniteMaterialBinding Material,
        string FamilySlotName,
        Func<IResoniteLinkClient, string, string, IReadOnlyDictionary<string, Member>, CancellationToken, Task<CreatedComponent>> CreateComponentAsync)
    {
        public Task<CreatedMaterialAsset> CreateLazySharedMaterialTask(string materialKey)
        {
            return CreateAsync(materialKey);
        }

        private async Task<CreatedMaterialAsset> CreateAsync(string materialKey)
        {
            CreatedSlot familySlot = await ResoniteMaterialPlanning.TryGetExistingSharedChildSlotAsync(
                Client,
                RunState.Context.CommonAssetsRootSlot.SlotId,
                FamilySlotName,
                RunState.Runtime.ProcessingCancellationToken)
                ?? await RunState.Placement.GetOrCreateSharedChildSlotAsync(
                    Client,
                    RunState.Context.CommonAssetsRootSlot.SlotId,
                    FamilySlotName,
                    RunState.Runtime.ProcessingCancellationToken);
            PlannedDedicatedMaterialAsset plannedMaterial = await MaterialPlanning.PlanCommonMaterialAssetAsync(
                Client,
                Material,
                RunState.Runtime.ProcessingCancellationToken);
            string materialSlotName = ResoniteSceneMaterialConventions.CreateMaterialSlotName(Material, useCommonMaterialAssets: true);
            string materialComponentType = ResoniteMaterialComponentPolicy.GetComponentType(Material);
            string? existingMaterialComponentId = await ResoniteMaterialPlanning.TryGetExistingCommonMaterialComponentIdAsync(
                Client,
                familySlot.SlotId,
                materialSlotName,
                materialComponentType,
                RunState.Runtime.ProcessingCancellationToken);
            if (!string.IsNullOrWhiteSpace(existingMaterialComponentId))
            {
                return new CreatedMaterialAsset(existingMaterialComponentId, null);
            }

            CreatedMaterialAsset createdMaterial = await ResoniteMaterialPlanning.EmitCommonMaterialAsync(
                Client,
                plannedMaterial,
                familySlot.SlotId,
                materialSlotName,
                RunState.Placement.GetOrCreateSharedChildSlotAsync,
                CreateComponentAsync,
                RunState.Runtime.ProcessingCancellationToken);
            return createdMaterial;
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
                    progressReporter,
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
                    progressReporter,
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

    private static long EstimateBatchPayloadBytes(int operationCount)
    {
        return Math.Max(1L, operationCount) * 1024L;
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

    private static async Task AwaitProcessingTasksIfCompletedAsync(LiveSendRunState state)
    {
        await state.Runtime.AwaitIfAnyTaskCompletedAsync();
    }

    private static void TryMarkProcessingFailure(LiveSendRunState state, Exception exception)
    {
        state.Runtime.TryMarkFailure(exception);
    }

    private static void CancelProcessing(LiveSendRunState state)
    {
        state.Runtime.Cancel();
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
            request.ResolvedSourcePath);
    }

    internal sealed record QueuedCityObject(
        ResoniteConstructionCityObject CityObject,
        Task<PreparedCityObject> PreparationTask,
        Task<ResoniteSharedSlotIndex.ObjectSlotHierarchy> ObjectHierarchyTask,
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
        TerrainTextureOverlay? TerrainOverlay = null,
        GeneratedTerrainTexture? GeneratedTerrainTexture = null);

}
