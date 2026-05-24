using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

public sealed class ResoniteLiveSceneImportTarget : ISceneSink
{
    private readonly Uri endpoint;
    private readonly int connectionCount;
    private readonly IResoniteCommonMaterialSetupPreparer commonMaterialSetupPreparer;
    private readonly ILiveSendRunPlanFactory runPlanFactory;
    private readonly ILiveSendRunStateFactory runStateFactory;
    private readonly IResoniteQueuedCityObjectWorker queuedCityObjectWorker;
    private readonly IResoniteQueuedCityObjectEnqueuer queuedCityObjectEnqueuer;
    private readonly IResoniteLiveSendFinalizer finalizer;
    private readonly IResoniteSlotCreator slotCreator;
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
        ArgumentNullException.ThrowIfNull(dependencies.RunStateFactory);

        endpoint = options.Endpoint;
        connectionCount = options.ConnectionCount;
        MemoryProfile = options.MemoryProfile;
        Diagnostics = dependencies.Diagnostics;
        MeshBakeEnabled = options.EnableMeshBake;
        progressReporter = options.ProgressReporter;
        sceneSetupInterpreter = dependencies.SceneSetupInterpreter;
        commonMaterialSetupPreparer = dependencies.CommonMaterialSetupPreparer;
        runPlanFactory = dependencies.RunPlanFactory;
        runStateFactory = dependencies.RunStateFactory;
        queuedCityObjectWorker = dependencies.QueuedCityObjectWorker;
        queuedCityObjectEnqueuer = dependencies.QueuedCityObjectEnqueuer;
        finalizer = dependencies.Finalizer;
        slotCreator = dependencies.SlotCreator;
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
                await queuedCityObjectEnqueuer.QueueUnitAsync(
                    state,
                    objectUnit,
                    CreateEnqueueContext(),
                    cancellationToken);
            }

            SceneImportExecutionResult result = await finalizer.CompleteAsync(
                state,
                CreateFinalizationContext(),
                cancellationToken);
            completedSuccessfully = true;
            return result;
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

        LiveSendRunPlan runPlan = runPlanFactory.Create(
            SetupInfo,
            workRoot,
            requestLocalOrigin,
            MemoryProfile,
            connectionCount,
            MeshBakeEnabled);
        ReportProgress(
            PlateauLog.Info(
                "live",
                $"Initializing scene state for dataset '{SetupInfo.Dataset}' "
                + $"mesh '{SetupInfo.MeshCode}' at '{runPlan.ResolvedWorkRoot}'."));
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

        await commonMaterialSetupPreparer.PrepareAsync(
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
        progress.Reset();
        LiveSendRunState state = runStateFactory.Create(
            runPlan,
            setupState,
            progress,
            materials,
            placement,
            cancellationToken);
        ResoniteImportBudgetProfile resourceBudget = runPlan.ResourceBudget;
        Stopwatch laneStartStopwatch = Stopwatch.StartNew();
        Diagnostics.StartSendWindow(connectionCount);
        state.Runtime.Start(queuedCityObjectWorker.CreateProcessingTasks(
            state,
            new LiveSendWorkerContext(
                endpoint,
                connectionCount,
                GetRoutedClient,
                Diagnostics,
                progressReporter)));
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

    private void ReportProgress(string message)
    {
        progressReporter?.Invoke(message);
    }

    private IResoniteLinkClient GetRoutedClient()
    {
        return ClientSessionInternal.GetRequiredClient();
    }

    private LiveSendEnqueueContext CreateEnqueueContext()
    {
        return new LiveSendEnqueueContext(
            connectionCount,
            GetRoutedClient,
            progressReporter);
    }

    private LiveSendFinalizationContext CreateFinalizationContext()
    {
        return new LiveSendFinalizationContext(
            endpoint,
            CreateEnqueueContext(),
            Diagnostics,
            progressReporter);
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

}
