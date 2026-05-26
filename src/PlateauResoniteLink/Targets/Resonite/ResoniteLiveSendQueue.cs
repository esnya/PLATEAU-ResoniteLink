using System;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSendQueue
{
    Task QueueUnitAsync(
        LiveSendRunState state,
        ImportedObjectUnit objectUnit,
        LiveSendEnqueueContext context,
        CancellationToken cancellationToken);

    Task<SceneImportExecutionResult> CompleteAsync(
        LiveSendRunState state,
        LiveSendFinalizationContext context,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteLiveSendQueue(
    IResoniteQueuedCityObjectEnqueuer enqueuer,
    IResoniteLiveSendFinalizer finalizer) : IResoniteLiveSendQueue
{
    private readonly IResoniteQueuedCityObjectEnqueuer enqueuer =
        enqueuer ?? throw new ArgumentNullException(nameof(enqueuer));
    private readonly IResoniteLiveSendFinalizer finalizer =
        finalizer ?? throw new ArgumentNullException(nameof(finalizer));

    public Task QueueUnitAsync(
        LiveSendRunState state,
        ImportedObjectUnit objectUnit,
        LiveSendEnqueueContext context,
        CancellationToken cancellationToken)
    {
        return enqueuer.QueueUnitAsync(
            state,
            objectUnit,
            context,
            cancellationToken);
    }

    public Task<SceneImportExecutionResult> CompleteAsync(
        LiveSendRunState state,
        LiveSendFinalizationContext context,
        CancellationToken cancellationToken)
    {
        return finalizer.CompleteAsync(
            state,
            context,
            cancellationToken);
    }
}
