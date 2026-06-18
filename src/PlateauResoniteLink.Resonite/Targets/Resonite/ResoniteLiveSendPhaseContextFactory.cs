using System;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class ResoniteLiveSendPhaseContextFactory
{
    public static LiveSendRunStartContext CreateRunStartContext(LiveSendRunExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new LiveSendRunStartContext(
            context.Endpoint,
            context.ClientSession,
            context.Diagnostics);
    }

    public static LiveSendEnqueueContext CreateEnqueueContext(LiveSendRunExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new LiveSendEnqueueContext(
            context.ConnectionCount,
            context.ClientSession.GetRequiredClient);
    }

    public static LiveSendFinalizationContext CreateFinalizationContext(
        LiveSendRunExecutionContext context,
        LiveSendEnqueueContext enqueueContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(enqueueContext);

        return new LiveSendFinalizationContext(
            context.Endpoint,
            enqueueContext,
            context.Diagnostics);
    }
}
