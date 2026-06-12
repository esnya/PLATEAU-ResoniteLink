using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class ResoniteLiveSendQueue
{
    public static Task QueueUnitAsync(
        LiveSendRunState state,
        ImportedObjectUnit objectUnit,
        LiveSendEnqueueContext context,
        CancellationToken cancellationToken)
    {
        return ResoniteQueuedCityObjectEnqueuer.QueueUnitAsync(
            state,
            objectUnit,
            context,
            cancellationToken);
    }

    public static Task<SceneImportExecutionResult> CompleteAsync(
        LiveSendRunState state,
        LiveSendFinalizationContext context,
        CancellationToken cancellationToken)
    {
        return ResoniteLiveSendFinalizer.CompleteAsync(
            state,
            context,
            cancellationToken);
    }
}
