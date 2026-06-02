using System;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record LiveSendRunStartRequest(
    ResoniteSceneSetupInfo SetupInfo,
    string WorkRoot,
    CommonMaterialCatalog<DefaultCommonMaterialMember> CommonMaterials,
    LiveSendConnectionRequest ConnectionRequest,
    ResoniteLocalOrigin RequestLocalOrigin,
    ResoniteImportMemoryProfile MemoryProfile,
    int ConnectionCount,
    bool MeshBakeEnabled);

internal sealed record LiveSendRunStartContext(
    Uri Endpoint,
    ILiveSendClientSession ClientSession,
    ResoniteLinkSendDiagnostics Diagnostics,
    Action<string>? ProgressReporter);

internal interface IResoniteLiveSendRunStarter
{
    Task<LiveSendRunState> StartAsync(
        LiveSendRunStartRequest request,
        LiveSendRunStartContext context,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteLiveSendRunStarter(
    ILiveSendRunPlanFactory runPlanFactory,
    IResoniteLiveSendConnectionInitializer connectionInitializer,
    IResoniteLiveSendRunSetupPreparer runSetupPreparer,
    ILiveSendRunStateFactory runStateFactory,
    IResoniteLiveSendWorkerLauncher workerLauncher) : IResoniteLiveSendRunStarter
{
    public async Task<LiveSendRunState> StartAsync(
        LiveSendRunStartRequest request,
        LiveSendRunStartContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request.SetupInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkRoot);
        ArgumentNullException.ThrowIfNull(request.CommonMaterials);
        ArgumentNullException.ThrowIfNull(request.ConnectionRequest);
        ArgumentNullException.ThrowIfNull(context.Endpoint);
        ArgumentNullException.ThrowIfNull(context.ClientSession);
        ArgumentNullException.ThrowIfNull(context.Diagnostics);

        LiveSendRunPlan runPlan = runPlanFactory.Create(
            request.SetupInfo,
            request.WorkRoot,
            request.RequestLocalOrigin,
            request.MemoryProfile,
            request.ConnectionCount,
            request.MeshBakeEnabled);
        await connectionInitializer.EnsureConnectedAsync(
            request,
            runPlan,
            context,
            cancellationToken);
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
}
