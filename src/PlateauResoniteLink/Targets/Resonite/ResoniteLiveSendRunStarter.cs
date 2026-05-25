using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSendRunStarter
{
    Task<LiveSendRunState> StartAsync(
        LiveSendRunStartRequest request,
        LiveSendRunStartContext context,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteLiveSendRunStarter(
    IResoniteLiveSendRunPlanInitializer runPlanInitializer,
    IResoniteLiveSendConnectionInitializer connectionInitializer,
    IResoniteLiveSendSetupInitializer setupInitializer,
    IResoniteLiveSendRunActivator runActivator) : IResoniteLiveSendRunStarter
{
    public async Task<LiveSendRunState> StartAsync(
        LiveSendRunStartRequest request,
        LiveSendRunStartContext context,
        CancellationToken cancellationToken)
    {
        LiveSendRunPlan runPlan = runPlanInitializer.Initialize(request, context);
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
}
