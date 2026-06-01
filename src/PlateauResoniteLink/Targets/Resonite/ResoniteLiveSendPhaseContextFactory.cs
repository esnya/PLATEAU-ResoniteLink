using System;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSendPhaseContextFactory
{
    LiveSendRunStartContext CreateRunStartContext(LiveSendRunExecutionContext context);

    LiveSendEnqueueContext CreateEnqueueContext(LiveSendRunExecutionContext context);

    LiveSendFinalizationContext CreateFinalizationContext(
        LiveSendRunExecutionContext context,
        LiveSendEnqueueContext enqueueContext);
}

internal sealed class ResoniteLiveSendPhaseContextFactory : IResoniteLiveSendPhaseContextFactory
{
    public LiveSendRunStartContext CreateRunStartContext(LiveSendRunExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new LiveSendRunStartContext(
            context.Endpoint,
            context.ClientSession,
            context.Diagnostics,
            context.ProgressReporter);
    }

    public LiveSendEnqueueContext CreateEnqueueContext(LiveSendRunExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new LiveSendEnqueueContext(
            context.ConnectionCount,
            context.ClientSession.GetRequiredClient,
            context.ProgressReporter);
    }

    public LiveSendFinalizationContext CreateFinalizationContext(
        LiveSendRunExecutionContext context,
        LiveSendEnqueueContext enqueueContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(enqueueContext);

        return new LiveSendFinalizationContext(
            context.Endpoint,
            enqueueContext,
            context.Diagnostics,
            context.ProgressReporter);
    }
}
