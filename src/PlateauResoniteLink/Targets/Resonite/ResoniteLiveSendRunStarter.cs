using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record LiveSendRunStartRequest
{
    public LiveSendRunStartRequest(
        ResoniteSceneSetupInfo SetupInfo,
        string WorkRoot,
        CommonMaterialCatalog<DefaultCommonMaterialMember> CommonMaterials,
        LiveSendConnectionRequest ConnectionRequest,
        ResoniteLocalOrigin RequestLocalOrigin,
        ResoniteImportMemoryProfile MemoryProfile,
        int ConnectionCount,
        bool MeshBakeEnabled)
    {
        ArgumentNullException.ThrowIfNull(SetupInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(WorkRoot);
        ArgumentNullException.ThrowIfNull(CommonMaterials);
        ArgumentNullException.ThrowIfNull(ConnectionRequest);
        ArgumentOutOfRangeException.ThrowIfLessThan(ConnectionCount, 1);

        this.SetupInfo = SetupInfo;
        this.WorkRoot = WorkRoot;
        this.CommonMaterials = CommonMaterials;
        this.ConnectionRequest = ConnectionRequest;
        this.RequestLocalOrigin = RequestLocalOrigin;
        this.MemoryProfile = MemoryProfile;
        this.ConnectionCount = ConnectionCount;
        this.MeshBakeEnabled = MeshBakeEnabled;
    }

    public ResoniteSceneSetupInfo SetupInfo { get; }

    public string WorkRoot { get; }

    public CommonMaterialCatalog<DefaultCommonMaterialMember> CommonMaterials { get; }

    public LiveSendConnectionRequest ConnectionRequest { get; }

    public ResoniteLocalOrigin RequestLocalOrigin { get; }

    public ResoniteImportMemoryProfile MemoryProfile { get; }

    public int ConnectionCount { get; }

    public bool MeshBakeEnabled { get; }
}

internal sealed record LiveSendRunStartContext
{
    public LiveSendRunStartContext(
        Uri Endpoint,
        ILiveSendClientSession ClientSession,
        ResoniteLinkSendDiagnostics Diagnostics,
        Action<string>? ProgressReporter)
    {
        ArgumentNullException.ThrowIfNull(Endpoint);
        ArgumentNullException.ThrowIfNull(ClientSession);
        ArgumentNullException.ThrowIfNull(Diagnostics);

        this.Endpoint = Endpoint;
        this.ClientSession = ClientSession;
        this.Diagnostics = Diagnostics;
        this.ProgressReporter = ProgressReporter;
    }

    public Uri Endpoint { get; }

    public ILiveSendClientSession ClientSession { get; }

    public ResoniteLinkSendDiagnostics Diagnostics { get; }

    public Action<string>? ProgressReporter { get; }
}

internal sealed class ResoniteLiveSendRunStarter(
    ResoniteLiveSendRunSetupPreparer runSetupPreparer,
    EnsureResoniteLiveSendConnected ensureConnected,
    ResoniteTextureImageLoader textureImageLoader,
    ResoniteQueuedCityObjectWorker queuedCityObjectWorker)
{
    private const int MaxQueuedCityObjects = 4;
    private const long MaxInFlightCityObjectWorkingSetBytesPerLane = 256L * 1024L * 1024L;
    private const long MaxInFlightCityObjectWorkingSetBytesFloor = 512L * 1024L * 1024L;

    public async Task<LiveSendRunState> StartAsync(
        LiveSendRunStartRequest request,
        LiveSendRunStartContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        LiveSendRunPlan runPlan = CreateRunPlan(request);
        await ensureConnected(
            request,
            runPlan,
            context,
            cancellationToken);
        LiveSendPreparedRunSetup preparedSetup = await runSetupPreparer.PrepareAsync(
            runPlan,
            request,
            context,
            cancellationToken);
        LiveSendRunState state = CreateRunState(
            preparedSetup.RunPlan,
            preparedSetup.SetupState,
            preparedSetup.Progress,
            preparedSetup.Materials,
            preparedSetup.Placement,
            cancellationToken);
        LaunchWorkers(
            state,
            preparedSetup.RunPlan.Queue,
            preparedSetup.RunPlan.ResourceBudget,
            context);
        return state;
    }

    private static LiveSendRunPlan CreateRunPlan(LiveSendRunStartRequest request)
    {
        ResoniteImportBudgetProfile resourceBudget = ResoniteImportBudgetProfiles.ForProfile(request.MemoryProfile);
        return new LiveSendRunPlan(
            request.SetupInfo,
            Path.GetFullPath(request.WorkRoot),
            request.RequestLocalOrigin,
            ResonitePlacementPolicy.CreateSourceFileSlotNamesByRelativePath(request.SetupInfo.SourceFiles),
            resourceBudget,
            new LiveSendQueuePlan(
                request.ConnectionCount,
                Math.Max(MaxQueuedCityObjects * request.ConnectionCount, request.ConnectionCount),
                Math.Max(
                    resourceBudget.ImportWorkingSetBytes,
                    Math.Max(
                        MaxInFlightCityObjectWorkingSetBytesFloor,
                        request.ConnectionCount * MaxInFlightCityObjectWorkingSetBytesPerLane))),
            request.MeshBakeEnabled);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership is transferred to LiveSendRunState and released by ResoniteLiveSendRunResourceReleaser.")]
    private LiveSendRunState CreateRunState(
        LiveSendRunPlan runPlan,
        ResoniteSceneSetupState setupState,
        LiveSendProgressSink progress,
        CommonMaterialAssetCache materials,
        ResoniteSharedSlotIndex placement,
        CancellationToken cancellationToken)
    {
        NonDemCityObjectBaker? cityObjectBaker = CreateCityObjectBaker(
            runPlan.MeshBakeEnabled,
            runPlan.ResourceBudget,
            runPlan.RequestLocalOrigin);
        LiveSendRunContext context = new(
            runPlan,
            setupState.DatasetRootSlot,
            setupState.DatasetAssetsRootSlot,
            setupState.CommonAssetsRootSlot,
            cityObjectBaker);

        return new LiveSendRunState
        {
            Context = context,
            Progress = progress,
            Materials = materials,
            TerrainTextures = new TerrainTextureAssetCache(),
            Placement = placement,
            Runtime = new LiveSendExecutionRuntime(runPlan.Queue, cancellationToken),
            GsiFallbackLicenseGate = new SemaphoreSlim(1, 1),
            DemSourceUseCounts = new ConcurrentDictionary<TerrainTextureSource, int>(),
        };
    }

    private NonDemCityObjectBaker? CreateCityObjectBaker(
        bool enableMeshBake,
        ResoniteImportBudgetProfile resourceBudget,
        ResoniteLocalOrigin requestLocalOrigin)
    {
        _ = resourceBudget.Name switch
        {
            ResoniteImportMemoryProfile.Small or ResoniteImportMemoryProfile.Large => true,
            _ => throw new ArgumentOutOfRangeException(nameof(resourceBudget), resourceBudget.Name, "Unsupported memory profile."),
        };

        return enableMeshBake
            ? new NonDemCityObjectBaker(
                bakePolicies: NonDemCityObjectBakePolicies.DefaultPolicies,
                sourceFileBakeEmitter: CreateSourceFileBakeEmitter(
                    new NonDemAtlasBakeBudget(ResourceBudget: resourceBudget),
                    requestLocalOrigin))
            : null;
    }

    private NonDemSourceFileBakeEmitter CreateSourceFileBakeEmitter(
        NonDemAtlasBakeBudget atlasBudget,
        ResoniteLocalOrigin requestLocalOrigin)
    {
        NonDemAtlasLayoutFactory layoutFactory = new(
            atlasBudget.EffectiveMaxAtlasSize,
            atlasBudget.TilePaddingPixels);
        return new NonDemSourceFileBakeEmitter(
            new NonDemCityObjectBakeCandidateFactory(
                new NonDemBakeEntryFactory(textureImageLoader, atlasBudget.EffectiveMaxAtlasTextureEdge)),
            new NonDemCityObjectBakeAssembler(
                layoutFactory,
                new NonDemAtlasImageRenderer(atlasBudget.TilePaddingPixels),
                requestLocalOrigin),
            new NonDemAtlasBatchFitPolicy(layoutFactory));
    }

    private void LaunchWorkers(
        LiveSendRunState state,
        LiveSendQueuePlan queuePlan,
        ResoniteImportBudgetProfile resourceBudget,
        LiveSendRunStartContext context)
    {
        int connectionCount = queuePlan.ConnectionCount;

        ReportProgress(
            context,
            PlateauLog.Info(
                "live",
                $"Starting routed send workers (connection_pool={connectionCount})."));
        state.Progress.Reset();
        Stopwatch laneStartStopwatch = Stopwatch.StartNew();
        context.Diagnostics.StartSendWindow(connectionCount);
        state.Runtime.Start(queuedCityObjectWorker.CreateProcessingTasks(
            state,
            new LiveSendWorkerContext(
                context.Endpoint,
                connectionCount,
                context.ClientSession.GetRequiredClient,
                context.Diagnostics,
                context.ProgressReporter)));
        ReportProgress(
            context,
            PlateauLog.Info(
                "live",
                $"Send lane tasks launched (connection budget={connectionCount}, "
                + $"queue_capacity_total={queuePlan.QueueCapacity}, "
                + $"memory_budget_bytes={queuePlan.MemoryBudgetBytes}, "
                + $"memory_profile={resourceBudget.Name.ToString().ToLowerInvariant()}, "
                + $"runtime_vram_budget_bytes={resourceBudget.RuntimeVramBudgetBytes})."));
        laneStartStopwatch.Stop();
        ReportProgress(
            context,
            PlateauLog.Info(
                "live",
                $"Send workers ready against connection pool={connectionCount}."));
        ReportProgress(
            context,
            PlateauLog.Info(
                "live",
                $"Send lane startup phase complete in {laneStartStopwatch.Elapsed.TotalSeconds:F2}s."));
    }

    private static void ReportProgress(
        LiveSendRunStartContext context,
        string message)
    {
        context.ProgressReporter?.Invoke(message);
    }
}
