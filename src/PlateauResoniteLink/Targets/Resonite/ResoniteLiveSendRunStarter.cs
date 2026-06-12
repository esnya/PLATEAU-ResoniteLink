using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Diagnostics;
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
        bool MeshBakeEnabled,
        bool DistanceCullingEnabled = false)
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
        this.DistanceCullingEnabled = DistanceCullingEnabled;
    }

    public ResoniteSceneSetupInfo SetupInfo { get; }

    public string WorkRoot { get; }

    public CommonMaterialCatalog<DefaultCommonMaterialMember> CommonMaterials { get; }

    public LiveSendConnectionRequest ConnectionRequest { get; }

    public ResoniteLocalOrigin RequestLocalOrigin { get; }

    public ResoniteImportMemoryProfile MemoryProfile { get; }

    public int ConnectionCount { get; }

    public bool MeshBakeEnabled { get; }

    public bool DistanceCullingEnabled { get; }
}

internal sealed record LiveSendRunStartContext
{
    public LiveSendRunStartContext(
        Uri Endpoint,
        ILiveSendClientSession ClientSession,
        ResoniteLinkSendDiagnostics Diagnostics,
        ILogger Logger)
    {
        ArgumentNullException.ThrowIfNull(Endpoint);
        ArgumentNullException.ThrowIfNull(ClientSession);
        ArgumentNullException.ThrowIfNull(Diagnostics);

        this.Endpoint = Endpoint;
        this.ClientSession = ClientSession;
        this.Diagnostics = Diagnostics;
        this.Logger = Logger;
    }

    public Uri Endpoint { get; }

    public ILiveSendClientSession ClientSession { get; }

    public ResoniteLinkSendDiagnostics Diagnostics { get; }

    public ILogger Logger { get; }
}

internal sealed class ResoniteLiveSendRunStarter(
    IResoniteLiveSendRunSetupPreparer runSetupPreparer,
    LiveSendRunStateFactory runStateFactory,
    ResoniteLiveSendWorkerLauncher workerLauncher)
{
    public async Task<LiveSendRunState> StartAsync(
        LiveSendRunStartRequest request,
        LiveSendRunStartContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        LiveSendRunPlan runPlan = LiveSendRunPlanFactory.Create(
            request.SetupInfo,
            request.WorkRoot,
            request.RequestLocalOrigin,
            request.MemoryProfile,
            request.ConnectionCount,
            request.MeshBakeEnabled,
            request.DistanceCullingEnabled);
        await EnsureConnectedAsync(request, runPlan, context, cancellationToken);
        LiveSendPreparedRunSetup preparedSetup = await runSetupPreparer.PrepareAsync(
            runPlan,
            request,
            context,
            cancellationToken);
        LiveSendRunState state = runStateFactory.Create(
            preparedSetup.RunPlan,
            preparedSetup.SetupState,
            preparedSetup.Progress,
            preparedSetup.Materials,
            preparedSetup.Placement,
            cancellationToken);
        workerLauncher.Launch(
            new LiveSendWorkerLaunchRequest(
                state,
                preparedSetup.RunPlan.Queue,
                preparedSetup.RunPlan.ResourceBudget),
            context);
        return state;
    }

    private static async Task EnsureConnectedAsync(
        LiveSendRunStartRequest request,
        LiveSendRunPlan runPlan,
        LiveSendRunStartContext context,
        CancellationToken cancellationToken)
    {
        context.Logger.WriteInformation(
            "Initializing scene state for dataset '{Dataset}' mesh '{MeshCode}' at '{ResolvedWorkRoot}'.",
            request.SetupInfo.Dataset,
            request.SetupInfo.MeshCode,
            runPlan.ResolvedWorkRoot);
        Stopwatch connectionStopwatch = Stopwatch.StartNew();
        context.Logger.WriteInformation(
            "Connecting ResoniteLink connection pool to {Endpoint} with {ConnectionCount} available routed connection(s).",
            context.Endpoint,
            request.ConnectionCount);
        await context.ClientSession.EnsureConnectedAsync(
            request.ConnectionRequest,
            cancellationToken);
        connectionStopwatch.Stop();
        context.Logger.WriteInformation(
            "ResoniteLink connection pool ready in {ElapsedSeconds:F2}s (dataset='{Dataset}', mesh='{MeshCode}').",
            connectionStopwatch.Elapsed.TotalSeconds,
            request.SetupInfo.Dataset,
            request.SetupInfo.MeshCode);
    }
}
