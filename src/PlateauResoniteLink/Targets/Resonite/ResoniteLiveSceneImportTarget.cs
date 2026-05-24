using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

public sealed class ResoniteLiveSceneImportTarget : ISceneSink
{
    private const int MaxQueuedCityObjects = 4;
    private const long MaxInFlightCityObjectWorkingSetBytesPerLane = 256L * 1024L * 1024L;
    private const long MaxInFlightCityObjectWorkingSetBytesFloor = 512L * 1024L * 1024L;
    private const string DemPackageName = "dem";
    private const string TerrainGridAssetSlotSuffix = "_terrain-grid";
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
    private readonly IResoniteSceneSetupInterpreter sceneSetupInterpreter;
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
        sceneSetupInterpreter = dependencies.SceneSetupInterpreter;
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

    internal ResoniteImportMemoryProfile MemoryProfile { get; }

    public async Task<SceneImportExecutionResult> ExecuteAsync(
        SceneImportExecutionPlan plan,
        IAsyncEnumerable<ImportedObjectUnit> objectUnits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(objectUnits);
        if (Interlocked.Exchange(ref executionClaimed, 1) != 0)
        {
            throw new InvalidOperationException("A live scene import run is already active on this live scene import target instance.");
        }
        bool completedSuccessfully = false;
        LiveSendRunState? state = null;

        try
        {
            SceneImportRequest request = plan.SceneImportRequest;
            state = await CreateRunStateAsync(
                CreateSceneSetupInfo(request),
                request.WorkRoot,
                request.CommonMaterials,
                plan.NormalizedRequest,
                CreateLocalOrigin(plan.SceneImportRequest.Metadata.GeodeticOrigin),
                cancellationToken);

            await foreach (ImportedObjectUnit objectUnit in objectUnits.WithCancellation(cancellationToken))
            {
                await QueueCityObjectUnitAsync(state, objectUnit, cancellationToken);
            }

            IReadOnlyList<string> destinations = await FinalizeRunAsync(state, cancellationToken);
            completedSuccessfully = true;
            return new SceneImportExecutionResult(
                destinations,
                state.Progress.ProcessedCityObjectCount,
                state.Progress.FailedCityObjectCount,
                CreateDataSourceUsages(state));
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
        ResoniteSceneSetupInfo SetupInfo,
        string workRoot,
        CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials,
        PlateauImportRequest normalizedRequest,
        ResoniteLocalOrigin requestLocalOrigin,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(SetupInfo);
        ArgumentNullException.ThrowIfNull(commonMaterials);
        ArgumentNullException.ThrowIfNull(normalizedRequest);
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);

        string resolvedWorkRoot = Path.GetFullPath(workRoot);
        LiveSendRunPlan runPlan = CreateRunPlan(SetupInfo, resolvedWorkRoot, requestLocalOrigin);
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Initializing scene state for dataset '{SetupInfo.Dataset}' "
                + $"mesh '{SetupInfo.MeshCode}' at '{resolvedWorkRoot}'."));
        Stopwatch connectionStopwatch = Stopwatch.StartNew();
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Connecting ResoniteLink connection pool to {endpoint} "
                + $"with {connectionCount} available routed connection(s)."));
        await ClientSessionInternal.EnsureConnectedAsync(
            new LiveSendConnectionRequest(
                normalizedRequest.Dataset,
                normalizedRequest.MeshCode),
            cancellationToken);
        connectionStopwatch.Stop();
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"ResoniteLink connection pool ready in {connectionStopwatch.Elapsed.TotalSeconds:F2}s "
                + $"(dataset='{SetupInfo.Dataset}', mesh='{SetupInfo.MeshCode}')."));
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
                "Starting setup slot setup: dataset root, assets root, common assets root, location slot, and source-file root reference."));
        Stopwatch setupStopwatch = Stopwatch.StartNew();
        ResoniteSceneSetupState setupState = await sceneSetupInterpreter.SetupAsync(
            routedClient,
            runPlan.SetupInfo,
            commonMaterials,
            cancellationToken);
        setupStopwatch.Stop();
        ResoniteSharedSlotIndex placement = new(
            setupState.DatasetRootSlot,
            setupState.DatasetAssetsRootSlot,
            runPlan.RequestLocalOrigin,
            runPlan.SourceFileSlotNamesByRelativePath,
            setupState.SceneAnchor,
            slotCreator.CreateAsync);
        placement.IndexSetupHierarchy(setupState);
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Scene setup complete in {setupStopwatch.Elapsed.TotalSeconds:F2}s "
                + $"(dataset_root={setupState.DatasetRootSlot.SlotName}, assets_root={setupState.DatasetAssetsRootSlot.SlotName}, "
                + $"common_root={setupState.CommonAssetsRootSlot.SlotName}, "
                + $"dataset_root_existed={setupState.DatasetRootExisted}, "
                + $"location_slot='{setupState.SceneAnchor.LocationSlot.Value}', "
                + $"anchor_mesh='{setupState.SceneAnchor.MeshCode}', "
                + $"anchor_source_file_root='{setupState.SceneAnchor.ReferenceSourceFileRoot?.Value ?? "<pending>"}')."));
        foreach (CommonMaterialCatalogMember<ResoniteCommonMaterialAsset> materialAsset in setupState.CommonMaterialAssets.EnumerateMembers())
        {
            materials.CommonMaterialAssets.Set(materialAsset.Item);
        }

        foreach (string family in setupState.CommonMaterialFamilies)
        {
            materials.CommonMaterialFamilyWarmupTasks[family] = Task.CompletedTask;
        }

        if (setupState.CommonMaterialAssets.Count > 0)
        {
            progress.FirstCommonMaterialPrepLogged = setupState.CommonMaterialAssets.Count;
            ReportProgress(
                PlateauLog.Info(
                    "live",
                    $"Setup batch prepared {setupState.CommonMaterialAssets.Count} textureless common materials."));
        }
        else
        {
            ReportProgress(PlateauLog.Info("live", "Setup created common material slots; no textureless common material components were needed in setup batch."));
        }

        await PrepareCommonMaterialAssetsDuringSetupAsync(
            GetRoutedClient(),
            setupState,
            materials,
            commonMaterials,
            cancellationToken);

        ReportProgress(
            PlateauLog.Info(
                "live",
                "setup fixed dataset license metadata/component before city-object streaming starts."));
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Dataset metadata/license phase complete during setup. "
                + $"Dataset root existed={setupState.DatasetRootExisted}."));
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
            setupState.DatasetRootSlot,
            setupState.DatasetAssetsRootSlot,
            setupState.CommonAssetsRootSlot,
            cityObjectBaker);
        LiveSendRunState state = new()
        {
            Context = context,
            Progress = progress,
            Materials = materials,
            TerrainTextures = new TerrainTextureAssetCache(),
            Placement = placement,
            Runtime = runtime,
            GsiFallbackLicenseGate = new SemaphoreSlim(1, 1),
            DemSourceUseCounts = new ConcurrentDictionary<string, int>(StringComparer.Ordinal),
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
        ResoniteSceneSetupInfo SetupInfo,
        string resolvedWorkRoot,
        ResoniteLocalOrigin requestLocalOrigin)
    {
        ResoniteImportBudgetProfile resourceBudget = ResoniteImportBudgetProfiles.ForProfile(MemoryProfile);
        return new LiveSendRunPlan(
            SetupInfo,
            resolvedWorkRoot,
            requestLocalOrigin,
            ResonitePlacementPolicy.CreateSourceFileSlotNamesByRelativePath(SetupInfo.SourceFiles),
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
            ReportProgress(PlateauLog.Warning("live", $"Send lane {laneIndex + 1}/{connectionCount} canceled."));
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
                    + $"sourceFile='{currentCityObject.CityObject.SourceFileRelativePath ?? "<null>"}'");
            ReportProgress(PlateauLog.Error("live", $"Send lane {laneIndex + 1}/{connectionCount} failed{cityObjectContext}: {exception.Message}"));
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
        ResoniteSceneSetupInfo SetupInfo = state.Context.Plan.SetupInfo;
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
                    + $"against routed connections to {endpoint} for dataset '{SetupInfo.Dataset}' mesh '{SetupInfo.MeshCode}'."));
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

    private async Task QueueCityObjectUnitAsync(
        LiveSendRunState state,
        ImportedObjectUnit objectUnit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(objectUnit);

        foreach (ImportedCityObject cityObject in objectUnit.CityObjects)
        {
            await QueueCityObjectAsync(state, SceneImportContractMapper.ToInternal(cityObject), cancellationToken);
        }

        CompositeCityObjectBaker? cityObjectBaker = state.Context.CityObjectBaker;
        if (cityObjectBaker is null)
        {
            return;
        }

        await FlushBufferedCityObjectsAsync(state, cityObjectBaker, cancellationToken);
    }

    private async Task<int> FlushBufferedCityObjectsAsync(
        LiveSendRunState state,
        CompositeCityObjectBaker cityObjectBaker,
        CancellationToken cancellationToken)
    {
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

        return bakedCityObjectCount;
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
            int bakedCityObjectCount = await FlushBufferedCityObjectsAsync(state, cityObjectBaker, cancellationToken);
            bakeFlushStopwatch.Stop();
            ReportProgress(
                PlateauLog.Info(
                    "live",
                    $"Buffered bake flush produced {bakedCityObjectCount} baked city objects "
                    + $"in {bakeFlushStopwatch.Elapsed.TotalSeconds:F3}s."));

            foreach ((string name, int inputCount, int outputCount) in cityObjectBaker.GetBakeSummaries().Where(static summary => summary.OutputCount > 0))
            {
                ReportProgress(
                    PlateauLog.Debug(
                        "live",
                        $"{name} batched {inputCount} input city objects "
                        + $"into {outputCount} baked batch objects."));
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

        return [$"{endpoint}#{state.Placement.SceneAnchor?.LocationSlot.Value ?? context.DatasetRootSlot.Locator.Value}"];
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
                CreatePreparationTask(state, queuedCityObject.CityObject, cancellationToken),
                cancellationToken);
            await ImportPreparedCityObjectAsync(state, queuedCityObject, preparedCityObject, cancellationToken);

            int processedCount = Interlocked.Increment(ref state.Progress.ProcessedCityObjectCount);
            ReportProgress(
                PlateauLog.Info(
                    "live",
                    $"Sent city object {processedCount}: "
                    + $"{preparedCityObject.CityObject.DisplayName} "
                    + $"({preparedCityObject.CityObject.PackageName}/{preparedCityObject.CityObject.SlotKey})"));
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
        return exception is ContinuableImportException
            || FindResoniteLinkOperationException(exception) is { OperationName: "ImportMesh" or "ImportTexture" or "GetSlot" or "GetComponent" };
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
                new QueuedCityObject(cityObject, objectHierarchyTask, cityObjectMemoryLease),
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
                EstimateTriangleMeshWorkingSetBytes(triangleMesh.Mesh, cityObject.Materials) * triangleMeshExpansionFactor),
            ResoniteTerrainGridGeometry heightMap => EstimateTerrainGridWorkingSetBytes(heightMap),
            ResoniteDynamicTerrainGeometry dynamicTerrain => checked(
                (EstimateTriangleMeshWorkingSetBytes(dynamicTerrain.StaticMesh.Mesh, cityObject.Materials) * triangleMeshExpansionFactor)
                + EstimateTerrainGridWorkingSetBytes(dynamicTerrain.GridMesh)),
            _ => minimumWeightBytes,
        };

        ResoniteTexturePayload[] distinctTexturePayloads = cityObject.Materials
            .Where(static material => material.TexturePayload is not null)
            .Select(static material => material.TexturePayload!)
            .Distinct(TexturePayloadReferenceComparer.Instance)
            .ToArray();
        long directTexturePayloadWeightBytes = distinctTexturePayloads.Sum(static payload => (long)payload.BinaryPayload.Length);
        long terrainOverlayWeightBytes = cityObject.Materials
            .Where(static material => material.TerrainOverlay is not null)
            .Select(static material => material.TerrainOverlay!)
            .Distinct()
            .Sum(EstimateTerrainOverlayWorkingSetBytes);
        long materialWeightBytes = checked(
            (cityObject.Materials.Count * materialBindingWeightBytes)
            + (distinctTexturePayloads.Length * textureReferenceWeightBytes)
            + directTexturePayloadWeightBytes
            + terrainOverlayWeightBytes);
        return Math.Max(minimumWeightBytes, geometryWeightBytes + materialWeightBytes);

        static long EstimateTerrainGridWorkingSetBytes(ResoniteTerrainGridGeometry heightMap)
        {
            return checked(
                (heightMap.HeightSamples.Count * heightSampleWeightBytes)
                + (((long)heightMap.Width * heightMap.Height * hdrHeightTextureWeightBytes) * heightMapExpansionFactor));
        }

        static long EstimateTriangleMeshWorkingSetBytes(
            ResoniteImportedMesh mesh,
            IReadOnlyList<ResoniteMaterialBinding> materials)
        {
            bool requiresUvNormalization = materials.Any(ResoniteDynamicMaterialUvNormalizer.ShouldNormalizeTextureTransform);
            long normalizedVertexCount = requiresUvNormalization
                ? mesh.Submeshes.Sum(static submesh => (long)submesh.TriangleVertexIndices.Count)
                : mesh.Vertices.Count;
            long sourceVertexCount = mesh.Vertices.Count;
            long vertexBytes = requiresUvNormalization
                ? checked((sourceVertexCount + normalizedVertexCount) * vertexWeightBytes)
                : sourceVertexCount * vertexWeightBytes;
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
        progressReporter?.Invoke(message);
    }

    private IResoniteLinkClient GetRoutedClient()
    {
        return ClientSessionInternal.GetRequiredClient();
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
        cityObject = ResoniteDynamicMaterialUvNormalizer.Normalize(cityObject);

        if (cityObject.Geometry is ResoniteTriangleMeshGeometry triangleGeometry)
        {
            try
            {
                ResoniteCityObjectPreparation.ValidateTriangleMeshBindings(cityObject, triangleGeometry.Mesh);
            }
            catch (Exception exception) when (exception is InvalidOperationException && exception is not ResoniteMeshValidationException)
            {
                throw new ResoniteMeshValidationException(
                    $"Triangle mesh '{cityObject.DisplayName}' failed sender-side validation. "
                    + $"{ResoniteCityObjectPreparation.CreateTriangleMeshDiagnosticSummary(cityObject, triangleGeometry.Mesh)} "
                    + $"Reason: {exception.Message}",
                    exception);
            }

        }
        else if (cityObject.Geometry is ResoniteDynamicTerrainGeometry dynamicTerrain)
        {
            try
            {
                ResoniteCityObjectPreparation.ValidateTriangleMeshBindings(cityObject, dynamicTerrain.StaticMesh.Mesh);
            }
            catch (Exception exception) when (exception is InvalidOperationException && exception is not ResoniteMeshValidationException)
            {
                throw new ResoniteMeshValidationException(
                    $"Triangle mesh '{cityObject.DisplayName}' failed sender-side validation. "
                    + $"{ResoniteCityObjectPreparation.CreateTriangleMeshDiagnosticSummary(cityObject, dynamicTerrain.StaticMesh.Mesh)} "
                    + $"Reason: {exception.Message}",
                    exception);
            }
        }
        (string TerrainMeshCode, TerrainTextureOverlay TerrainOverlay)[] distinctTerrainOverlays = cityObject.Materials
            .Select((material, materialIndex) => (Material: material, MaterialIndex: materialIndex))
            .Where(static entry => entry.Material.TerrainOverlay is not null && entry.Material.TerrainMeshCode is not null)
            .Select(entry => (
                TerrainMeshCode: ValidateTerrainTextureMeshCode(
                    cityObject,
                    entry.MaterialIndex,
                    entry.Material,
                    entry.Material.TerrainMeshCode!,
                    entry.Material.TerrainOverlay!),
                TerrainOverlay: entry.Material.TerrainOverlay!))
            .Distinct()
            .OrderBy(static entry => entry.TerrainMeshCode, StringComparer.Ordinal)
            .ThenBy(static entry => entry.TerrainOverlay.PackageName, StringComparer.Ordinal)
            .ThenBy(static entry => entry.TerrainOverlay.GeographicBounds.MinLatitude)
            .ThenBy(static entry => entry.TerrainOverlay.GeographicBounds.MinLongitude)
            .ToArray();

        Task<PreparedTextureReference?>[] terrainOverlayTexturePreparationTasks = distinctTerrainOverlays
            .Select(entry => PrepareTerrainOverlayTextureReferenceAsync(
                state,
                entry.TerrainMeshCode,
                entry.TerrainOverlay,
                cancellationToken))
            .ToArray();
        Task<PreparedTextureReference?>[] texturePreparationTasks = [];

        Task<PreparedConstructionGeometry> geometryPreparationTask = cityObject.Geometry switch
        {
            ResoniteTriangleMeshGeometry triangleMesh => Task.Run<PreparedConstructionGeometry>(
                () => ResoniteCityObjectPreparation.PrepareTriangleMeshGeometry(cityObject, triangleMesh.Mesh),
                cancellationToken),
            ResoniteTerrainGridGeometry heightMap => Task.Run<PreparedConstructionGeometry>(
                () => new PreparedTerrainGridGeometry(heightMap, ResoniteCityObjectPreparation.PrepareTerrainGridDisplacementTexture(heightMap)),
                cancellationToken),
            ResoniteDynamicTerrainGeometry dynamicTerrain => Task.Run<PreparedConstructionGeometry>(
                () => new PreparedDynamicTerrainGeometry(
                    ResoniteCityObjectPreparation.PrepareTriangleMeshGeometry(cityObject, dynamicTerrain.StaticMesh.Mesh),
                    new PreparedTerrainGridGeometry(
                        dynamicTerrain.GridMesh,
                        ResoniteCityObjectPreparation.PrepareTerrainGridDisplacementTexture(dynamicTerrain.GridMesh))),
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
        Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay = preparedTextures
            .Where(static texture => texture is { TerrainOverlay: not null, GeneratedTerrainTexture: not null })
            .ToDictionary(
                static texture => texture.TerrainOverlay!,
                static texture => texture.GeneratedTerrainTexture!);
        cityObject = ResoniteCityObjectPreparation.ApplyTerrainTextureCanvasUv(
            cityObject,
            preparedTerrainTextureDataByOverlay,
            clampCanvasUv: IsDemPackage(cityObject.PackageName));
        if (cityObject.Geometry is ResoniteTriangleMeshGeometry resolvedTriangleMesh
            && preparedGeometry is PreparedTriangleMeshGeometry)
        {
            preparedGeometry = ResoniteCityObjectPreparation.PrepareTriangleMeshGeometry(cityObject, resolvedTriangleMesh.Mesh);
        }
        else if (cityObject.Geometry is ResoniteDynamicTerrainGeometry resolvedDynamicTerrain
            && preparedGeometry is PreparedDynamicTerrainGeometry preparedDynamicTerrain)
        {
            preparedGeometry = preparedDynamicTerrain with
            {
                StaticMesh = ResoniteCityObjectPreparation.PrepareTriangleMeshGeometry(cityObject, resolvedDynamicTerrain.StaticMesh.Mesh),
            };
        }
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
        string terrainMeshCode,
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken)
    {
        GeneratedTerrainTexture terrainTexture = await terrainTextureAssetGenerator.EnsureTextureAsync(
            terrainTextureOverlay,
            cancellationToken);
        TerrainTextureSource[] usedSources = GetTrackedTerrainTextureSources(terrainTexture, terrainTextureOverlay);
        foreach (TerrainTextureSource usedSource in usedSources)
        {
            int useCount = state.DemSourceUseCounts.AddOrUpdate(
                usedSource.IdentityKey,
                1,
                static (_, current) => checked(current + 1));
            if (useCount == 1)
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
        }

        return new PreparedTextureReference(
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            TextureImport: terrainTexture.TextureImport,
            TerrainMeshCode: terrainMeshCode,
            TerrainOverlay: terrainTextureOverlay,
            GeneratedTerrainTexture: terrainTexture);
    }

    private static TerrainTextureSource[] GetTrackedTerrainTextureSources(
        GeneratedTerrainTexture terrainTexture,
        TerrainTextureOverlay terrainTextureOverlay)
    {
        if (terrainTexture.UsedSources is { Count: > 0 })
        {
            return terrainTexture.UsedSources
                .Distinct()
                .ToArray();
        }

        return
        [
            terrainTexture.UsedSource ?? terrainTextureOverlay.PrimarySource,
        ];
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
        return DemTerrainTextureDefaults.IsGsiFallbackSource(source);
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

    private async Task PrepareCommonMaterialAssetsDuringSetupAsync(
        IResoniteLinkClient client,
        ResoniteSceneSetupState setupState,
        CommonMaterialAssetCache materials,
        CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials,
        CancellationToken cancellationToken)
    {
        CommonMaterialCatalog<ResoniteCommonMaterialPlan> commonMaterialPlans =
            ResoniteCommonMaterialPlans.CreateCatalogPlans(commonMaterials);
        if (commonMaterialPlans.Count == 0)
        {
            ReportProgress(PlateauLog.Info("live", "No common material assets are required during scene setup."));
            return;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        int preparedCount = 0;
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Preparing {commonMaterialPlans.Count} common material assets during scene setup before object streaming."));
        ResoniteBatchOperations.BatchActionBuilder batchBuilder = new();
        List<PreparedCommonMaterialBatchEntry> preparedMaterials = [];
        foreach (CommonMaterialCatalogMember<ResoniteCommonMaterialPlan> catalogMember in commonMaterialPlans.EnumerateMembers())
        {
            ResoniteCommonMaterialPlan materialPlan = catalogMember.Item;
            cancellationToken.ThrowIfCancellationRequested();
            ResoniteMaterialBinding material = materialPlan.Material;
            string familySlotName = ResoniteSceneMaterialConventions.GetCommonMaterialFamilySlotName(material);
            string materialSlotName = materialPlan.SlotName;
            if (!setupState.CommonMaterialFamilies.Contains(familySlotName))
            {
                throw new InvalidOperationException(
                    $"Setup did not create common material family '{familySlotName}' before common asset preparation.");
            }

            if (materials.CommonMaterialAssets.TryGetAsset(materialPlan.Member, out _))
            {
                preparedCount++;
                continue;
            }

            Stopwatch materialStopwatch = Stopwatch.StartNew();
            ReportProgress(
                PlateauLog.Info(
                    "live",
                    $"Preparing common material asset {preparedCount + 1}/{commonMaterialPlans.Count}: "
                    + $"family='{familySlotName}', slot='{materialSlotName}'."));
            CreatedSlot familySlot = await FindRequiredCommonMaterialFamilySlotAsync(
                client,
                setupState.CommonAssetsRootSlot,
                familySlotName,
                cancellationToken);
            (CreatedSlot? reusableSlot, ResoniteComponentLocator? existingComponent) = await TryFindReusableCommonMaterialSlotAsync(
                client,
                familySlot,
                ResoniteSceneMaterialConventions.CreateCommonMaterialSlotLookupNames(material),
                ResoniteMaterialComponentPolicy.GetComponentType(material),
                cancellationToken);
            if (existingComponent is not null)
            {
                materials.CommonMaterialAssets.Set(new ResoniteCommonMaterialAsset(
                    materialPlan.Member,
                    material,
                    new CreatedMaterialAsset(existingComponent.Value, null)));
                preparedCount++;
                ReportProgress(
                    PlateauLog.Info(
                        "live",
                        $"Reused common material asset {preparedCount}/{commonMaterialPlans.Count}: "
                        + $"family='{familySlotName}', slot='{materialSlotName}', elapsed_s={materialStopwatch.Elapsed.TotalSeconds:F2}."));
                continue;
            }

            PlannedDedicatedMaterialAsset plannedMaterial = await materialPlanning.PlanCommonMaterialAssetAsync(
                client,
                material,
                materials.BundledTextureImportTasks,
                cancellationToken);
            string materialContainerSlotId;
            ResoniteBatchOperations.PendingBatchSlot? pendingMaterialSlot = null;
            if (reusableSlot is null)
            {
                pendingMaterialSlot = batchBuilder.AddSlot(
                    familySlot.Locator.Value,
                    materialSlotName,
                    null,
                    null);
                materialContainerSlotId = pendingMaterialSlot.Value.LocalId.Value;
            }
            else
            {
                materialContainerSlotId = reusableSlot.Value.Locator.Value;
            }

            ResoniteBatchOperations.PendingBatchComponent pendingMaterialComponent =
                ResoniteMaterialPlanning.AddCommonMaterialComponents(
                    batchBuilder,
                    plannedMaterial,
                    materialContainerSlotId);
            preparedMaterials.Add(new PreparedCommonMaterialBatchEntry(
                materialPlan,
                material,
                pendingMaterialSlot,
                pendingMaterialComponent));
            preparedCount++;
            ReportProgress(
                PlateauLog.Info(
                    "live",
                    $"Planned common material asset {preparedCount}/{commonMaterialPlans.Count}: "
                    + $"family='{familySlotName}', slot='{materialSlotName}', texture_import_elapsed_s={materialStopwatch.Elapsed.TotalSeconds:F2}."));
        }

        if (batchBuilder.Actions.Count > 0)
        {
            BatchResponse response = await client.RunDataModelOperationBatchAsync(batchBuilder.Actions, cancellationToken);
            CanonicalBatchEntityMap entityMap = CanonicalBatchEntityMap.Create(response);
            foreach (PreparedCommonMaterialBatchEntry preparedMaterial in preparedMaterials)
            {
                if (preparedMaterial.PendingMaterialSlot is not null)
                {
                    _ = entityMap.ResolveSlot(preparedMaterial.PendingMaterialSlot.Value);
                }

                CreatedComponent createdMaterialComponent = entityMap.ResolveComponent(preparedMaterial.PendingMaterialComponent);
                materials.CommonMaterialAssets.Set(new ResoniteCommonMaterialAsset(
                    preparedMaterial.MaterialPlan.Member,
                    preparedMaterial.Material,
                    new CreatedMaterialAsset(createdMaterialComponent.Locator, null)));
            }

            ReportProgress(
                PlateauLog.Info(
                    "live",
                    $"Created {preparedMaterials.Count} common material assets in one setup component batch."));
        }

        foreach (string family in setupState.CommonMaterialFamilies)
        {
            materials.CommonMaterialFamilyWarmupTasks[family] = Task.CompletedTask;
        }

        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Prepared {preparedCount} common material assets during scene setup in {stopwatch.Elapsed.TotalSeconds:F2}s."));
    }

    private readonly record struct PreparedCommonMaterialBatchEntry(
        ResoniteCommonMaterialPlan MaterialPlan,
        ResoniteMaterialBinding Material,
        ResoniteBatchOperations.PendingBatchSlot? PendingMaterialSlot,
        ResoniteBatchOperations.PendingBatchComponent PendingMaterialComponent);

    private Task<PreparedTextureReference?> PrepareDirectMaterialTextureReferenceAsync(
        ResoniteMaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(material.TexturePayload);

        return Task.FromResult<PreparedTextureReference?>(
            new PreparedTextureReference(
                TexturePayload: material.TexturePayload,
                TextureSourceKind: material.TextureSourceKind,
                TextureImport: ResoniteTextureImportFactory.CreateRawFromPayload(material.TexturePayload),
                TerrainMeshCode: null,
                TerrainOverlay: null));
    }

    private async Task ImportPreparedCityObjectAsync(
        LiveSendRunState state,
        QueuedCityObject queuedCityObject,
        PreparedCityObject preparedCityObject,
        CancellationToken cancellationToken)
    {
        ResoniteConstructionCityObject cityObject = preparedCityObject.CityObject;
        using ResoniteLinkSendDiagnostics.CityObjectSendScope sendScope = Diagnostics.BeginCityObjectSend(cityObject.PackageName);
        Stopwatch cityObjectStopwatch = Stopwatch.StartNew();
        ReportImportStep(cityObject, "Creating object slot hierarchy.");
        Stopwatch slotHierarchyStopwatch = Stopwatch.StartNew();
        ResoniteSharedSlotIndex.ObjectSlotHierarchy objectSlots = await AwaitWithSlowCityObjectWarningAsync(
            queuedCityObject.ObjectHierarchyTask,
            cancellationToken);
        slotHierarchyStopwatch.Stop();
        using CancellationTokenSource importStepCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IResoniteLinkClient routedClient = GetRoutedClient();
        Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay =
            CreatePreparedTerrainTextureDataByOverlay(preparedCityObject);
        Task<UploadedTextureAssetSet> uploadedTextureAssetsTask = UploadPreparedTexturesAsync(
            state,
            routedClient,
            preparedCityObject,
            preparedTerrainTextureDataByOverlay,
            importStepCancellation.Token);
        Stopwatch geometryStopwatch = Stopwatch.StartNew();
        Task<PlannedGeometryAsset> geometryPlanningTask = PlanGeometryAssetAsync(
            routedClient,
            cityObject,
            preparedCityObject,
            preparedTerrainTextureDataByOverlay,
            importStepCancellation.Token);
        Stopwatch materialStopwatch = new();
        Task<PlannedSceneMaterialPlan>? materialPlanningTask = null;
        PlannedSceneMaterialPlan plannedMaterials;
        PlannedGeometryAsset plannedGeometryAsset;
        try
        {
            UploadedTextureAssetSet uploadedTextureAssets = await uploadedTextureAssetsTask;
            materialStopwatch.Start();
            materialPlanningTask = PlanSceneMaterialPlanAsync(
                state,
                routedClient,
                cityObject,
                uploadedTextureAssets.TextureUrisByPayload,
                uploadedTextureAssets.TerrainTextureUrisByOverlay,
                uploadedTextureAssets.TerrainTexturePropertyBlockComponentsByMeshCode,
                uploadedTextureAssets.GeneratedTerrainTexturesByOverlay,
                importStepCancellation.Token);
            plannedMaterials = await materialPlanningTask;
            materialStopwatch.Stop();

            ReportImportStep(cityObject, $"Preparing geometry assets ({DescribePreparedGeometry(preparedCityObject.Geometry)}).");
            plannedGeometryAsset = await geometryPlanningTask;
            geometryStopwatch.Stop();
        }
        catch
        {
            await importStepCancellation.CancelAsync();
            IEnumerable<Task> tasksToObserve = materialPlanningTask is null
                ? [uploadedTextureAssetsTask, geometryPlanningTask]
                : [uploadedTextureAssetsTask, materialPlanningTask, geometryPlanningTask];
            await ObserveTaskFailuresAsync(tasksToObserve);
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

        ReportImportStep(cityObject, "Creating object-scoped DataModel batch.");
        Stopwatch batchStopwatch = Stopwatch.StartNew();
        await batchEmitter.ExecuteAsync(
            routedClient,
            cityObject,
            batchEmission,
            progressReporter,
            cancellationToken);
        batchStopwatch.Stop();

        ReportImportStep(cityObject, "Live import completed.");
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
        if (Interlocked.CompareExchange(ref state.Progress.FirstImportedCityObjectLogged, 1, 0) == 0)
        {
            ReportProgress(
                PlateauLog.Debug(
                    "live",
                    $"First city object imported after {GetSceneElapsedSeconds(state):F3}s: "
                    + $"{cityObject.DisplayName} ({cityObject.PackageName}/{cityObject.SlotKey})"));
        }
    }

    private static async Task<UploadedTextureAssetSet> UploadPreparedTexturesAsync(
        LiveSendRunState state,
        IResoniteLinkClient importClient,
        PreparedCityObject preparedCityObject,
        Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay,
        CancellationToken cancellationToken)
    {
        Dictionary<ResoniteTexturePayload, Uri> textureUrisByPayload = new(TexturePayloadReferenceComparer.Instance);
        Dictionary<TerrainTextureOverlay, Uri> terrainTextureUrisByOverlay = [];
        Dictionary<string, ResoniteComponentLocator> terrainTexturePropertyBlockComponentsByMeshCode = new(StringComparer.Ordinal);
        HashSet<ResoniteTexturePayload> queuedPayloads = new(TexturePayloadReferenceComparer.Instance);
        List<(PreparedTextureReference Texture, Task<Uri> ImportTask)> textureImportTasks = [];
        List<(PreparedTextureReference Texture, Task<SharedTerrainTextureAsset> ImportTask)> terrainTextureImportTasks = [];

        foreach (PreparedTextureReference texture in preparedCityObject.Textures)
        {
            if (texture.TexturePayload is not null && !queuedPayloads.Add(texture.TexturePayload))
            {
                continue;
            }

            if (texture is { TerrainOverlay: not null, GeneratedTerrainTexture: not null })
            {
                string meshCode = ResolveTerrainTextureMeshCode(texture);
                terrainTextureImportTasks.Add((
                    texture,
                    EnsureSharedTerrainTextureAssetAsync(
                        state,
                        importClient,
                        meshCode,
                        texture.TextureImport,
                        cancellationToken)));
                continue;
            }

            textureImportTasks.Add((
                texture,
                importClient.ImportTextureAsync(texture.TextureImport, cancellationToken)));
        }

        await Task.WhenAll(textureImportTasks.Select(static textureImport => textureImport.ImportTask));
        await Task.WhenAll(terrainTextureImportTasks.Select(static textureImport => textureImport.ImportTask));

        foreach ((PreparedTextureReference texture, Task<Uri> importTask) in textureImportTasks)
        {
            Uri textureUri = await importTask;
            if (texture.TexturePayload is not null)
            {
                textureUrisByPayload.Add(texture.TexturePayload, textureUri);
            }

        }

        foreach ((PreparedTextureReference texture, Task<SharedTerrainTextureAsset> importTask) in terrainTextureImportTasks)
        {
            SharedTerrainTextureAsset sharedTexture = await importTask;
            string meshCode = ResolveTerrainTextureMeshCode(texture);
            terrainTextureUrisByOverlay.Add(texture.TerrainOverlay!, sharedTexture.TextureUri);
            terrainTexturePropertyBlockComponentsByMeshCode.TryAdd(meshCode, sharedTexture.MainTexturePropertyBlockComponent.Locator);
        }

        return new UploadedTextureAssetSet(
            textureUrisByPayload,
            terrainTextureUrisByOverlay,
            terrainTexturePropertyBlockComponentsByMeshCode,
            preparedTerrainTextureDataByOverlay);
    }

    private static Task<SharedTerrainTextureAsset> EnsureSharedTerrainTextureAssetAsync(
        LiveSendRunState state,
        IResoniteLinkClient importClient,
        string meshCode,
        ResoniteTextureImport textureImport,
        CancellationToken cancellationToken)
    {
        return state.TerrainTextures.AssetsByMeshCode.GetOrCreateAsync(
            meshCode,
            () => EnsureSharedTerrainTextureAssetCoreAsync(state, importClient, meshCode, textureImport, cancellationToken),
            cancellationToken);
    }

    private static async Task<SharedTerrainTextureAsset> EnsureSharedTerrainTextureAssetCoreAsync(
        LiveSendRunState state,
        IResoniteLinkClient importClient,
        string meshCode,
        ResoniteTextureImport textureImport,
        CancellationToken cancellationToken)
    {
        CreatedSlot terrainTexturesRoot = await state.Placement.GetOrCreateSharedChildSlotAsync(
            importClient,
            state.Context.DatasetAssetsRootSlot.Locator,
            "Terrain Textures",
            cancellationToken);
        CreatedSlot meshSlot = await state.Placement.GetOrCreateSharedChildSlotAsync(
            importClient,
            terrainTexturesRoot.Locator,
            meshCode,
            cancellationToken);
        SharedTerrainTextureAsset? existingTexture = await TryFindSharedTerrainTextureAssetAsync(
            importClient,
            meshSlot,
            cancellationToken);
        if (existingTexture is not null)
        {
            Uri refreshedTextureUri = await importClient.ImportTextureAsync(textureImport, cancellationToken);
            await importClient.UpdateComponentAsync(
                new ResoniteComponentUpdate
                {
                    Component = new ResoniteTransportComponentLocator(existingTexture.TextureComponent.Locator.Value),
                    Members = ResoniteSceneMaterialConventions.CreateTextureMembers(
                        refreshedTextureUri,
                        ResoniteSceneMaterialConventions.TextureMemberRole.TerrainMainTextureOverride),
                },
                cancellationToken);
            return existingTexture with
            {
                TextureUri = refreshedTextureUri,
            };
        }

        Uri importedTextureUri = await importClient.ImportTextureAsync(textureImport, cancellationToken);
        CreatedComponent textureComponent = await ResoniteMaterialPlanning.CreateComponentAsync(
            importClient,
            meshSlot.Locator,
            "[FrooxEngine]FrooxEngine.StaticTexture2D",
            ResoniteSceneMaterialConventions.CreateTextureMembers(
                importedTextureUri,
                ResoniteSceneMaterialConventions.TextureMemberRole.TerrainMainTextureOverride),
            cancellationToken);
        CreatedComponent propertyBlockComponent = await CreateTerrainMainTexturePropertyBlockAsync(
            importClient,
            meshSlot.Locator,
            textureComponent.Locator,
            cancellationToken);
        return new SharedTerrainTextureAsset(
            importedTextureUri,
            textureComponent,
            propertyBlockComponent);
    }

    private static async Task<SharedTerrainTextureAsset?> TryFindSharedTerrainTextureAssetAsync(
        IResoniteLinkClient importClient,
        CreatedSlot meshSlot,
        CancellationToken cancellationToken)
    {
        Slot? slot = await importClient.GetSlotAsync(
            new ResoniteTransportSlotLocator(meshSlot.Locator.Value),
            depth: 0,
            cancellationToken);
        Component? textureComponent = slot?.Components?
            .FirstOrDefault(IsSharedTerrainTextureComponent);
        if (textureComponent?.ID is null
            || textureComponent.Members["URL"] is not Field_Uri url)
        {
            return null;
        }

        if (url.Value is null)
        {
            return null;
        }

        CreatedComponent createdTextureComponent = new(new ResoniteComponentLocator(textureComponent.ID), textureComponent.ComponentType);
        Component? propertyBlockComponent = slot?.Components?
            .Where(component => IsSharedTerrainMainTexturePropertyBlockComponent(component, textureComponent.ID))
            .OrderBy(static component => component.ID, StringComparer.Ordinal)
            .FirstOrDefault();
        CreatedComponent createdPropertyBlockComponent = propertyBlockComponent?.ID is null
            ? await CreateTerrainMainTexturePropertyBlockAsync(
                importClient,
                meshSlot.Locator,
                createdTextureComponent.Locator,
                cancellationToken)
            : new CreatedComponent(new ResoniteComponentLocator(propertyBlockComponent.ID), propertyBlockComponent.ComponentType);

        return new SharedTerrainTextureAsset(
            url.Value,
            createdTextureComponent,
            createdPropertyBlockComponent);
    }

    private static Task<CreatedComponent> CreateTerrainMainTexturePropertyBlockAsync(
        IResoniteLinkClient importClient,
        ResoniteSlotLocator meshSlot,
        ResoniteComponentLocator textureComponent,
        CancellationToken cancellationToken)
    {
        return ResoniteMaterialPlanning.CreateComponentAsync(
            importClient,
            meshSlot,
            "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock",
            new Dictionary<string, Member>(StringComparer.Ordinal)
            {
                ["Texture"] = new Reference
                {
                    TargetID = textureComponent.Value,
                },
            },
            cancellationToken);
    }

    private static bool IsSharedTerrainMainTexturePropertyBlockComponent(Component component, string textureComponentId)
    {
        return string.Equals(
                component.ComponentType,
                "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock",
                StringComparison.Ordinal)
            && component.Members.TryGetValue("Texture", out Member? textureMember)
            && textureMember is Reference { TargetID: string targetId }
            && string.Equals(targetId, textureComponentId, StringComparison.Ordinal);
    }

    private static bool IsSharedTerrainTextureComponent(Component component)
    {
        return string.Equals(
                component.ComponentType,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                StringComparison.Ordinal)
            && component.Members.TryGetValue("URL", out Member? urlMember)
            && urlMember is Field_Uri
            && component.Members.TryGetValue("WrapModeU", out Member? wrapModeUMember)
            && wrapModeUMember is Field_Enum { Value: "Clamp" }
            && component.Members.TryGetValue("WrapModeV", out Member? wrapModeVMember)
            && wrapModeVMember is Field_Enum { Value: "Clamp" };
    }

    private static string ResolveTerrainTextureMeshCode(PreparedTextureReference texture)
    {
        if (texture.TerrainMeshCode is not { Length: > 0 } meshCode
            || meshCode.Length != 8
            || !PlateauMeshCode.TryGetBounds(meshCode, out _))
        {
            throw new InvalidOperationException(
                "Terrain texture overlay preparation requires a valid third-level mesh-code. "
                + $"provided_mesh='{texture.TerrainMeshCode ?? "<null>"}'.");
        }

        return meshCode;
    }

    private static ResoniteMaterialBinding ValidateTerrainTextureMaterialContract(
        ResoniteConstructionCityObject cityObject,
        int materialIndex,
        ResoniteMaterialBinding material)
    {
        if (material.TerrainOverlay is null)
        {
            return material with { TerrainMeshCode = null };
        }

        if (material.TerrainMeshCode is null)
        {
            throw CreateTerrainOverlayMaterialContractException(
                cityObject,
                materialIndex,
                material,
                "missing terrain mesh-code");
        }

        return material with
        {
            TerrainMeshCode = ValidateTerrainTextureMeshCode(
                cityObject,
                materialIndex,
                material,
                material.TerrainMeshCode,
                material.TerrainOverlay),
        };
    }

    private static string ValidateTerrainTextureMeshCode(
        ResoniteConstructionCityObject cityObject,
        int materialIndex,
        ResoniteMaterialBinding material,
        string meshCode,
        TerrainTextureOverlay terrainOverlay)
    {
        if (meshCode.Length == 8
            && PlateauMeshCode.TryGetBounds(meshCode, out (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) bounds)
            && BoundsApproximatelyEqual(bounds, terrainOverlay.GeographicBounds))
        {
            return meshCode;
        }

        throw CreateTerrainOverlayMaterialContractException(
            cityObject,
            materialIndex,
            material,
            "mesh-code bounds do not match overlay bounds");
    }

    private static InvalidOperationException CreateTerrainOverlayMaterialContractException(
        ResoniteConstructionCityObject cityObject,
        int materialIndex,
        ResoniteMaterialBinding material,
        string reason)
    {
        TerrainTextureOverlay? overlay = material.TerrainOverlay;
        string overlaySummary = overlay is null
            ? "<null>"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"package='{overlay.PackageName}', bounds='{FormatGeographicBounds(overlay.GeographicBounds)}', sources='{overlay.SourceDescriptorKey}'");
        return new InvalidOperationException(
            "Terrain overlay material requires a third-level mesh-code that matches the overlay geographic bounds. "
            + $"reason='{reason}', object_slot='{cityObject.SlotKey}', object_name='{cityObject.DisplayName}', "
            + $"package='{cityObject.PackageName}', actual_mesh_code='{cityObject.ActualMeshCode}', source_file='{cityObject.SourceFileRelativePath ?? "<null>"}', "
            + $"material_index='{materialIndex}', terrain_mesh='{material.TerrainMeshCode ?? "<null>"}', overlay={overlaySummary}.");
    }

    private static string FormatGeographicBounds(GeographicRectangle bounds)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{FormatRounded(bounds.MinLatitude)}-{FormatRounded(bounds.MaxLatitude)}-{FormatRounded(bounds.MinLongitude)}-{FormatRounded(bounds.MaxLongitude)}");
    }

    private static string FormatRounded(double value)
    {
        return value.ToString("G17", CultureInfo.InvariantCulture);
    }

    private static bool BoundsApproximatelyEqual(
        (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) bounds,
        GeographicRectangle geographicBounds)
    {
        const double tolerance = 1e-8;
        return Math.Abs(bounds.SouthLatitude - geographicBounds.MinLatitude) <= tolerance
            && Math.Abs(bounds.NorthLatitude - geographicBounds.MaxLatitude) <= tolerance
            && Math.Abs(bounds.WestLongitude - geographicBounds.MinLongitude) <= tolerance
            && Math.Abs(bounds.EastLongitude - geographicBounds.MaxLongitude) <= tolerance;
    }

    private static Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> CreatePreparedTerrainTextureDataByOverlay(
        PreparedCityObject preparedCityObject)
    {
        Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> generatedTerrainTexturesByOverlay = [];
        foreach (PreparedTextureReference texture in preparedCityObject.Textures)
        {
            if (texture is { TerrainOverlay: not null, GeneratedTerrainTexture: not null })
            {
                generatedTerrainTexturesByOverlay.TryAdd(texture.TerrainOverlay, texture.GeneratedTerrainTexture);
            }
        }

        return generatedTerrainTexturesByOverlay;
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

    private static ResoniteMaterialBinding ResolveTerrainTextureMaterialForEmission(
        ResoniteConstructionCityObject cityObject,
        ResoniteMaterialBinding material,
        IReadOnlyDictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay)
    {
        _ = preparedTerrainTextureDataByOverlay;
        return (cityObject.Geometry is ResoniteTerrainGridGeometry
                || cityObject.Geometry is ResoniteDynamicTerrainGeometry)
            && material.TerrainOverlay is not null
            ? material with
            {
                TextureScale = null,
                TextureOffset = null,
            }
            : material;
    }

    private async Task<PlannedSceneMaterialPlan> PlanSceneMaterialPlanAsync(
        LiveSendRunState state,
        IResoniteLinkClient importClient,
        ResoniteConstructionCityObject cityObject,
        IReadOnlyDictionary<ResoniteTexturePayload, Uri> preparedTextureUrisByPayload,
        IReadOnlyDictionary<TerrainTextureOverlay, Uri> preparedTerrainTextureUrisByOverlay,
        IReadOnlyDictionary<string, ResoniteComponentLocator> preparedTerrainTexturePropertyBlockComponentsByMeshCode,
        IReadOnlyDictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay,
        CancellationToken cancellationToken)
    {
        Task<(PlannedMaterialAsset MaterialAsset, PlannedRendererMaterialBinding RendererBinding)>[] materialPlanTasks
            = new Task<(PlannedMaterialAsset MaterialAsset, PlannedRendererMaterialBinding RendererBinding)>[cityObject.Materials.Count];
        for (int materialIndex = 0; materialIndex < cityObject.Materials.Count; materialIndex++)
        {
            ResoniteMaterialBinding material = ResolveTerrainTextureMaterialForEmission(
                cityObject,
                cityObject.Materials[materialIndex],
                preparedTerrainTextureDataByOverlay);
            material = ValidateTerrainTextureMaterialContract(cityObject, materialIndex, material);
            ReportImportStep(
                cityObject,
                $"Creating material {materialIndex + 1}/{cityObject.Materials.Count}.");
            if (material.CommonMaterial is not null)
            {
                materialPlanTasks[materialIndex] = PlanSharedCommonRendererMaterialAsync(
                    state,
                    material,
                    preparedTextureUrisByPayload,
                    preparedTerrainTextureUrisByOverlay,
                    preparedTerrainTexturePropertyBlockComponentsByMeshCode,
                    cancellationToken);
                continue;
            }

            materialPlanTasks[materialIndex] = PlanDedicatedRendererMaterialAsync(
                importClient,
                material,
                materialIndex,
                cityObject.PackageName,
                preparedTextureUrisByPayload,
                preparedTerrainTextureUrisByOverlay,
                preserveDedicatedMaterialSlot: IsDemPackage(cityObject.PackageName),
                cancellationToken);
        }

        (PlannedMaterialAsset MaterialAsset, PlannedRendererMaterialBinding RendererBinding)[] materialPlans = await Task.WhenAll(materialPlanTasks);
        return new PlannedSceneMaterialPlan(
            materialPlans.Select(static plan => plan.MaterialAsset).ToArray(),
            materialPlans.Select(static plan => plan.RendererBinding).ToArray());

        async Task<(PlannedMaterialAsset MaterialAsset, PlannedRendererMaterialBinding RendererBinding)> PlanSharedCommonRendererMaterialAsync(
            LiveSendRunState runState,
            ResoniteMaterialBinding sourceMaterial,
            IReadOnlyDictionary<ResoniteTexturePayload, Uri> preparedTextureUrisByPayload,
            IReadOnlyDictionary<TerrainTextureOverlay, Uri> preparedTerrainTextureUrisByOverlay,
            IReadOnlyDictionary<string, ResoniteComponentLocator> terrainTexturePropertyBlockComponentsByMeshCode,
            CancellationToken ct)
        {
            DefaultCommonMaterialMember member = sourceMaterial.CommonMaterial
                ?? throw new InvalidOperationException("Common renderer material requires a typed common material member.");
            string familySlotName = ResoniteSceneMaterialConventions.GetCommonMaterialFamilySlotName(
                SceneImportContractMapper.ToInternal(member.CreateBinding([0])));
            if (runState.Materials.CommonMaterialFamilyWarmupTasks.TryGetValue(familySlotName, out Task? familyWarmupTask))
            {
                await familyWarmupTask.WaitAsync(ct);
            }

            if (!runState.Materials.CommonMaterialAssets.TryGetAsset(member, out CreatedMaterialAsset existingMaterialAsset))
            {
                throw new InvalidOperationException(
                    $"Setup did not resolve common material ({ResoniteMaterialComponentPolicy.DescribeForDiagnostics(sourceMaterial)}) before runtime emission.");
            }
            PlannedReusableMaterialAsset sharedMaterialAsset = new(
                existingMaterialAsset.MaterialComponent);
            PlannedTextureAsset? mainTextureOverride = await ResoniteMaterialPlanning.PlanMainTextureOverrideAsync(
                sourceMaterial,
                preparedTextureUrisByPayload,
                preparedTerrainTextureUrisByOverlay);
            PlannedRendererMaterialBinding rendererBinding = mainTextureOverride is null
                ? new PlannedDirectRendererMaterialBinding(sharedMaterialAsset)
                : CreateMainTextureOverrideRendererBinding(
                    sharedMaterialAsset,
                    mainTextureOverride,
                    terrainTexturePropertyBlockComponentsByMeshCode,
                    sourceMaterial);
            return (sharedMaterialAsset, rendererBinding);
        }

        async Task<(PlannedMaterialAsset MaterialAsset, PlannedRendererMaterialBinding RendererBinding)> PlanDedicatedRendererMaterialAsync(
            IResoniteLinkClient client,
            ResoniteMaterialBinding sourceMaterial,
            int materialIndex,
            string packageName,
            IReadOnlyDictionary<ResoniteTexturePayload, Uri> preparedTextureUrisByPayload,
            IReadOnlyDictionary<TerrainTextureOverlay, Uri> preparedTerrainTextureUrisByOverlay,
            bool preserveDedicatedMaterialSlot,
            CancellationToken ct)
        {
            ResoniteMaterialBinding materialComponentSource = sourceMaterial.TerrainOverlay is null
                ? sourceMaterial
                : sourceMaterial with
                {
                    TerrainOverlay = null,
                    TerrainMeshCode = null,
                    TextureScale = null,
                    TextureOffset = null,
                };
            PlannedDedicatedMaterialAsset plannedMaterial = await materialPlanning.PlanDedicatedMaterialAssetAsync(
                client,
                materialComponentSource,
                materialIndex,
                packageName,
                preparedTextureUrisByPayload,
                preparedTerrainTextureUrisByOverlay,
                preserveDedicatedMaterialSlot,
                ct);
            if (sourceMaterial.TerrainOverlay is not null)
            {
                PlannedTextureAsset? mainTextureOverride = await ResoniteMaterialPlanning.PlanMainTextureOverrideAsync(
                    sourceMaterial,
                    preparedTextureUrisByPayload,
                    preparedTerrainTextureUrisByOverlay);
                if (mainTextureOverride is not null)
                {
                    return (
                        plannedMaterial,
                        CreateMainTextureOverrideRendererBinding(
                            plannedMaterial,
                            mainTextureOverride,
                            preparedTerrainTexturePropertyBlockComponentsByMeshCode,
                            sourceMaterial));
                }
            }

            return (plannedMaterial, new PlannedDirectRendererMaterialBinding(plannedMaterial));
        }
    }

    private static PlannedMainTextureOverrideRendererMaterialBinding CreateMainTextureOverrideRendererBinding(
        PlannedMaterialAsset materialAsset,
        PlannedTextureAsset mainTexture,
        IReadOnlyDictionary<string, ResoniteComponentLocator> terrainTexturePropertyBlockComponentsByMeshCode,
        ResoniteMaterialBinding sourceMaterial)
    {
        if (sourceMaterial.TerrainOverlay is null)
        {
            return new PlannedAlbedoMainTextureOverrideRendererMaterialBinding(materialAsset, mainTexture);
        }

        return new PlannedTerrainMainTextureOverrideRendererMaterialBinding(
            materialAsset,
            mainTexture,
            null,
            sourceMaterial.TerrainMeshCode is not null
            && terrainTexturePropertyBlockComponentsByMeshCode.TryGetValue(sourceMaterial.TerrainMeshCode, out ResoniteComponentLocator propertyBlockComponent)
                ? propertyBlockComponent
                : null);
    }

    private static async Task<CreatedSlot> FindRequiredCommonMaterialFamilySlotAsync(
        IResoniteLinkClient client,
        CreatedSlot commonAssetsRootSlot,
        string familySlotName,
        CancellationToken cancellationToken)
    {
        Slot? commonRootSnapshot = await client.GetSlotAsync(
            new ResoniteTransportSlotLocator(commonAssetsRootSlot.Locator.Value),
            2,
            cancellationToken);
        if (commonRootSnapshot is not null)
        {
            ResoniteSceneChildLookupResult lookup = new ResoniteSceneSlotSnapshot(commonRootSnapshot)
                .GetUniqueChildLookupResult(familySlotName, commonAssetsRootSlot.Locator.Value);
            if (lookup.State == ResoniteSceneChildLookupState.FoundWithId && lookup.Slot is not null)
            {
                return new CreatedSlot(
                    new ResoniteSlotLocator(lookup.Slot.ID ?? throw new InvalidOperationException("Common material family slot did not expose an ID.")),
                    lookup.Slot.Name?.Value ?? familySlotName);
            }
        }

        throw new InvalidOperationException(
            $"Setup did not create common material family slot '{familySlotName}' before runtime emission.");
    }

    private static async Task<(CreatedSlot? ReusableSlot, ResoniteComponentLocator? ExistingComponent)> TryFindReusableCommonMaterialSlotAsync(
        IResoniteLinkClient client,
        CreatedSlot familySlot,
        IReadOnlyList<string> lookupNames,
        string materialComponentType,
        CancellationToken cancellationToken)
    {
        if (lookupNames.Count == 0)
        {
            return (null, null);
        }

        Slot? familySlotSnapshot = await client.GetSlotAsync(new ResoniteTransportSlotLocator(familySlot.Locator.Value), 1, cancellationToken);
        if (familySlotSnapshot is null)
        {
            return (null, null);
        }

        ResoniteSceneSlotSnapshot familySlotView = new(familySlotSnapshot);
        CreatedSlot? reusableSlotWithoutComponent = null;
        foreach (string materialSlotName in lookupNames.Where(static name => !string.IsNullOrWhiteSpace(name)))
        {
            ResoniteSceneChildLookupResult materialLookup = familySlotView.GetUniqueChildLookupResult(materialSlotName, familySlot.Locator.Value);
            if (materialLookup.State != ResoniteSceneChildLookupState.FoundWithId || materialLookup.Slot is null)
            {
                continue;
            }

            string? existingComponentId = materialLookup.Slot.Components?
                .Where(component => string.Equals(component.ComponentType, materialComponentType, StringComparison.Ordinal))
                .OrderBy(static component => component.ID, StringComparer.Ordinal)
                .Select(static component => component.ID)
                .FirstOrDefault(static id => !string.IsNullOrWhiteSpace(id));
            CreatedSlot reusableSlot = new(
                new ResoniteSlotLocator(materialLookup.Slot.ID!),
                materialLookup.Slot.Name?.Value ?? materialSlotName);
            if (!string.IsNullOrWhiteSpace(existingComponentId))
            {
                return (reusableSlot, new ResoniteComponentLocator(existingComponentId));
            }

            if (materialLookup.Slot.Components?.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Common material slot '{materialSlotName}' exists but does not contain material component '{materialComponentType}'. "
                    + "Remove the incomplete common material slot before retrying.");
            }

            reusableSlotWithoutComponent ??= reusableSlot;
        }

        return (reusableSlotWithoutComponent, null);
    }

    private static bool IsDemPackage(string packageName)
    {
        return string.Equals(packageName, DemPackageName, StringComparison.OrdinalIgnoreCase);
    }

    private static ImportDataSourceUsage[] CreateDataSourceUsages(LiveSendRunState state)
    {
        return state.DemSourceUseCounts
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => new ImportDataSourceUsage(
                ImportDataSourceCategory.DemTextureSource,
                pair.Key,
                pair.Value))
            .ToArray();
    }

    private async Task<PlannedGeometryAsset> PlanGeometryAssetAsync(
        IResoniteLinkClient importClient,
        ResoniteConstructionCityObject cityObject,
        PreparedCityObject preparedCityObject,
        IReadOnlyDictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay,
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
            PreparedTerrainGridGeometry heightMap => CreatePlannedGeometryAsset(
                cityObject,
                await geometryAssetAssembler.PrepareTerrainGridAsync(
                    importClient,
                    CreateMeshAssetSlotName(cityObject),
                    CreateTerrainGridAssetSlotName(cityObject),
                    cityObject.DisplayName,
                    heightMap.Geometry,
                    heightMap.HeightTextureImport,
                    ResoniteCityObjectPreparation.ResolveTerrainGridUvScale(cityObject, heightMap.Geometry, preparedTerrainTextureDataByOverlay),
                    ResoniteCityObjectPreparation.ResolveTerrainGridUvOffset(cityObject, heightMap.Geometry, preparedTerrainTextureDataByOverlay),
                    progressReporter,
                    cancellationToken)),
            PreparedDynamicTerrainGeometry dynamicTerrain => CreatePlannedDynamicTerrainGeometryAsset(
                cityObject,
                AssertUploadedTriangleMeshAssetBatch(await geometryAssetAssembler.PrepareTriangleMeshAsync(
                    importClient,
                    CreateMeshAssetSlotName(cityObject),
                    cityObject.DisplayName,
                    dynamicTerrain.StaticMesh.MeshImport,
                    progressReporter,
                    cancellationToken)),
                AssertUploadedTerrainGridAssetBatch(await geometryAssetAssembler.PrepareTerrainGridAsync(
                    importClient,
                    CreateMeshAssetSlotName(cityObject),
                    CreateTerrainGridAssetSlotName(cityObject),
                    cityObject.DisplayName,
                    dynamicTerrain.GridMesh.Geometry,
                    dynamicTerrain.GridMesh.HeightTextureImport,
                    ResoniteCityObjectPreparation.ResolveTerrainGridUvScale(cityObject, dynamicTerrain.GridMesh.Geometry, preparedTerrainTextureDataByOverlay),
                    ResoniteCityObjectPreparation.ResolveTerrainGridUvOffset(cityObject, dynamicTerrain.GridMesh.Geometry, preparedTerrainTextureDataByOverlay),
                    progressReporter,
                    cancellationToken))),
            _ => throw new InvalidOperationException(
                $"Unsupported prepared geometry type '{preparedCityObject.Geometry.GetType().Name}'."),
        };
    }

    private static PlannedGeometryAsset CreatePlannedGeometryAsset(
        ResoniteConstructionCityObject cityObject,
        UploadedGeometryAssetBatch uploadedGeometryBatch)
    {
        GeometryIdentity identity = new(
            string.Create(
                CultureInfo.InvariantCulture,
                $"geometry-{cityObject.PackageName}-{cityObject.SlotKey}-{uploadedGeometryBatch.MeshAssetSlotName}"));

        return uploadedGeometryBatch switch
        {
            UploadedTriangleMeshAssetBatch triangleMesh => new PlannedTriangleMeshGeometryAsset(
                identity,
                triangleMesh.MeshAssetSlotName,
                triangleMesh.MeshUri),
            UploadedTerrainGridAssetBatch heightMap => new PlannedTerrainGridGeometryAsset(
                identity,
                heightMap.MeshAssetSlotName,
                heightMap.TerrainGridAssetSlotName,
                heightMap.Geometry,
                heightMap.HeightTextureUri,
                heightMap.UvScale,
                heightMap.UvOffset),
            _ => throw new InvalidOperationException(
                $"Unsupported uploaded geometry asset batch type '{uploadedGeometryBatch.GetType().Name}'."),
        };
    }

    private static PlannedDynamicTerrainGeometryAsset CreatePlannedDynamicTerrainGeometryAsset(
        ResoniteConstructionCityObject cityObject,
        UploadedTriangleMeshAssetBatch staticMeshBatch,
        UploadedTerrainGridAssetBatch gridMeshBatch)
    {
        GeometryIdentity identity = new(
            string.Create(
                CultureInfo.InvariantCulture,
                $"geometry-{cityObject.PackageName}-{cityObject.SlotKey}-{staticMeshBatch.MeshAssetSlotName}"));

        return new PlannedDynamicTerrainGeometryAsset(
            identity,
            staticMeshBatch.MeshAssetSlotName,
            staticMeshBatch.MeshUri,
            gridMeshBatch.TerrainGridAssetSlotName,
            gridMeshBatch.Geometry,
            gridMeshBatch.HeightTextureUri,
            gridMeshBatch.UvScale,
            gridMeshBatch.UvOffset);
    }

    private static UploadedTriangleMeshAssetBatch AssertUploadedTriangleMeshAssetBatch(
        UploadedGeometryAssetBatch uploadedGeometryBatch)
    {
        return uploadedGeometryBatch as UploadedTriangleMeshAssetBatch
            ?? throw new InvalidOperationException(
                $"Unsupported uploaded static terrain asset batch type '{uploadedGeometryBatch.GetType().Name}'.");
    }

    private static UploadedTerrainGridAssetBatch AssertUploadedTerrainGridAssetBatch(
        UploadedGeometryAssetBatch uploadedGeometryBatch)
    {
        return uploadedGeometryBatch as UploadedTerrainGridAssetBatch
            ?? throw new InvalidOperationException(
                $"Unsupported uploaded terrain grid asset batch type '{uploadedGeometryBatch.GetType().Name}'.");
    }

    private static long EstimateBatchPayloadBytes(int operationCount)
    {
        return Math.Max(1L, operationCount) * 1024L;
    }

    private void ReportImportStep(ResoniteConstructionCityObject cityObject, string step)
    {
        ReportProgress(
            PlateauLog.Debug(
                "live",
                $"Importing '{cityObject.DisplayName}' ({cityObject.PackageName}/{cityObject.SlotKey}): {step}"));
    }

    private static string DescribePreparedGeometry(PreparedConstructionGeometry geometry)
    {
        return geometry switch
        {
            PreparedTriangleMeshGeometry triangleMesh =>
                $"triangle-mesh(vertices={triangleMesh.MeshImport.VertexCount}, submeshes={triangleMesh.MeshImport.Submeshes.Count})",
            PreparedTerrainGridGeometry heightMap =>
                $"terrain-grid({heightMap.Geometry.Width}x{heightMap.Geometry.Height})",
            PreparedDynamicTerrainGeometry dynamicTerrain =>
                $"dynamic-terrain(static={dynamicTerrain.StaticMesh.MeshImport.VertexCount} vertices, grid={dynamicTerrain.GridMesh.Geometry.Width}x{dynamicTerrain.GridMesh.Geometry.Height})",
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

    private static string CreateTerrainGridAssetSlotName(ResoniteConstructionCityObject cityObject)
    {
        return string.Concat(CreateMeshAssetSlotName(cityObject), TerrainGridAssetSlotSuffix);
    }

    private static ResoniteSceneSetupInfo CreateSceneSetupInfo(SceneImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ResoniteSceneSetupInfo(
            request.Metadata.Request.Dataset,
            request.Metadata.Request.MeshCode,
            request.Metadata.SourceDataset.SourceFiles,
            request.Metadata.SourceDataset.SelectedMeshCodes ?? [],
            new ResoniteLicenseAttributionMetadata(
                request.Metadata.Attribution.DatasetLicense.RequireCredit,
                request.Metadata.Attribution.DatasetLicense.CreditText,
                request.Metadata.Attribution.DatasetLicense.LicenseName,
                request.Metadata.Attribution.DatasetLicense.LicenseUrl));
    }

    private static ResoniteLocalOrigin CreateLocalOrigin(GeodeticOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        return new ResoniteLocalOrigin(origin.Latitude, origin.Longitude, origin.Altitude);
    }

    internal sealed record QueuedCityObject(
        ResoniteConstructionCityObject CityObject,
        Task<ResoniteSharedSlotIndex.ObjectSlotHierarchy> ObjectHierarchyTask,
        AsyncWeightedGate.Lease MemoryLease);

    private sealed record UploadedTextureAssetSet(
        Dictionary<ResoniteTexturePayload, Uri> TextureUrisByPayload,
        Dictionary<TerrainTextureOverlay, Uri> TerrainTextureUrisByOverlay,
        Dictionary<string, ResoniteComponentLocator> TerrainTexturePropertyBlockComponentsByMeshCode,
        Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> GeneratedTerrainTexturesByOverlay);

    private sealed class TexturePayloadReferenceComparer : IEqualityComparer<ResoniteTexturePayload>
    {
        internal static readonly TexturePayloadReferenceComparer Instance = new();

        public bool Equals(ResoniteTexturePayload? x, ResoniteTexturePayload? y) => ReferenceEquals(x, y);

        public int GetHashCode(ResoniteTexturePayload obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
