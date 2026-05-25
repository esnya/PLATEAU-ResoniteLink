using System;

using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSendContextFactory
{
    LiveSendRunStartContext CreateRunStart(ResoniteLiveSendTargetContext context);

    LiveSendEnqueueContext CreateEnqueue(ResoniteLiveSendTargetContext context);

    LiveSendFinalizationContext CreateFinalization(ResoniteLiveSendTargetContext context);
}

internal sealed class ResoniteLiveSendContextFactory : IResoniteLiveSendContextFactory
{
    public LiveSendRunStartContext CreateRunStart(ResoniteLiveSendTargetContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new LiveSendRunStartContext(
            context.Endpoint,
            context.ClientSession,
            context.Diagnostics,
            context.ProgressReporter);
    }

    public LiveSendEnqueueContext CreateEnqueue(ResoniteLiveSendTargetContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new LiveSendEnqueueContext(
            context.ConnectionCount,
            context.ClientSession.GetRequiredClient,
            context.ProgressReporter);
    }

    public LiveSendFinalizationContext CreateFinalization(ResoniteLiveSendTargetContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new LiveSendFinalizationContext(
            context.Endpoint,
            CreateEnqueue(context),
            context.Diagnostics,
            context.ProgressReporter);
    }
}

internal sealed record ResoniteLiveSendTargetContext(
    Uri Endpoint,
    int ConnectionCount,
    ILiveSendClientSession ClientSession,
    ResoniteLinkSendDiagnostics Diagnostics,
    Action<string>? ProgressReporter);
