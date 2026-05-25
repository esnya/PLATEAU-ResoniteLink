using System;
using System.Threading;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSendRunActivator
{
    LiveSendRunState Activate(
        LiveSendRunPlan runPlan,
        LiveSendSetupInitialization setup,
        LiveSendRunStartRequest request,
        LiveSendRunStartContext context,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteLiveSendRunActivator(
    ILiveSendRunStateFactory runStateFactory,
    IResoniteLiveSendWorkerLauncher workerLauncher) : IResoniteLiveSendRunActivator
{
    public LiveSendRunState Activate(
        LiveSendRunPlan runPlan,
        LiveSendSetupInitialization setup,
        LiveSendRunStartRequest request,
        LiveSendRunStartContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runPlan);
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        LiveSendRunState state = runStateFactory.Create(
            runPlan,
            setup.SetupState,
            setup.Progress,
            setup.Materials,
            setup.Placement,
            cancellationToken);
        workerLauncher.Launch(
            new LiveSendWorkerLaunchRequest(
                state,
                runPlan.Queue,
                runPlan.ResourceBudget,
                request.ConnectionCount),
            context);
        return state;
    }
}
