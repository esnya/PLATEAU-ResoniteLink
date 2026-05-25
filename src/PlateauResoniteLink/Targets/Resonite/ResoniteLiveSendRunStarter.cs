using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record LiveSendRunStartRequest(
    ResoniteSceneSetupInfo SetupInfo,
    string WorkRoot,
    CommonMaterialCatalog<DefaultCommonMaterialMember> CommonMaterials,
    PlateauImportRequest NormalizedRequest,
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
        ArgumentNullException.ThrowIfNull(request.NormalizedRequest);
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
        ReportProgress(
            context,
            PlateauLog.Info(
                "live",
                $"Initializing scene state for dataset '{request.SetupInfo.Dataset}' "
                + $"mesh '{request.SetupInfo.MeshCode}' at '{runPlan.ResolvedWorkRoot}'."));
        Stopwatch connectionStopwatch = Stopwatch.StartNew();
        ReportProgress(
            context,
            PlateauLog.Info(
                "live",
                $"Connecting ResoniteLink connection pool to {context.Endpoint} "
                + $"with {request.ConnectionCount} available routed connection(s)."));
        await context.ClientSession.EnsureConnectedAsync(
            new LiveSendConnectionRequest(
                request.NormalizedRequest.Dataset,
                request.NormalizedRequest.MeshCode),
            cancellationToken);
        connectionStopwatch.Stop();
        ReportProgress(
            context,
            PlateauLog.Info(
                "live",
                $"ResoniteLink connection pool ready in {connectionStopwatch.Elapsed.TotalSeconds:F2}s "
                + $"(dataset='{request.SetupInfo.Dataset}', mesh='{request.SetupInfo.MeshCode}')."));
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

    private static void ReportProgress(
        LiveSendRunStartContext context,
        string message)
    {
        context.ProgressReporter?.Invoke(message);
    }
}
