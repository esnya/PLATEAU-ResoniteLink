using System;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Logging;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSendRunStarter
{
    Task<LiveSendRunState> StartAsync(
        LiveSendRunStartRequest request,
        LiveSendRunStartContext context,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteLiveSendRunStarter(
    IResoniteLiveSendConnectionInitializer connectionInitializer,
    IResoniteLiveSendSetupInitializer setupInitializer,
    ILiveSendRunPlanFactory runPlanFactory,
    IResoniteLiveSendRunActivator runActivator) : IResoniteLiveSendRunStarter
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
        await connectionInitializer.EnsureConnectedAsync(request, context, cancellationToken);
        LiveSendSetupInitialization setup = await setupInitializer.InitializeAsync(
            request,
            context,
            runPlan,
            cancellationToken);
        return runActivator.Activate(
            runPlan,
            setup,
            request,
            context,
            cancellationToken);
    }

    private static void ReportProgress(
        LiveSendRunStartContext context,
        string message)
    {
        context.ProgressReporter?.Invoke(message);
    }
}
