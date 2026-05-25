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
        ArgumentNullException.ThrowIfNull(context.Endpoint);
        ArgumentNullException.ThrowIfNull(context.ClientSession);
        ArgumentNullException.ThrowIfNull(context.Diagnostics);

        return new LiveSendRunStartContext(
            context.Endpoint,
            context.ClientSession,
            context.Diagnostics,
            context.ProgressReporter);
    }

    public LiveSendEnqueueContext CreateEnqueueContext(LiveSendRunExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfLessThan(context.ConnectionCount, 1);
        ArgumentNullException.ThrowIfNull(context.ClientSession);

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
        ArgumentNullException.ThrowIfNull(context.Endpoint);
        ArgumentNullException.ThrowIfNull(context.Diagnostics);
        ArgumentNullException.ThrowIfNull(enqueueContext);

        return new LiveSendFinalizationContext(
            context.Endpoint,
            enqueueContext,
            context.Diagnostics,
            context.ProgressReporter);
    }
}
