using System;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteQueuedCityObjectWorker
{
    Task[] CreateProcessingTasks(
        LiveSendRunState state,
        LiveSendWorkerContext context);
}

internal sealed class ResoniteQueuedCityObjectWorker(
    IResoniteQueuedCityObjectLaneProcessor laneProcessor) : IResoniteQueuedCityObjectWorker
{
    private readonly IResoniteQueuedCityObjectLaneProcessor laneProcessor =
        laneProcessor ?? throw new ArgumentNullException(nameof(laneProcessor));

    public Task[] CreateProcessingTasks(
        LiveSendRunState state,
        LiveSendWorkerContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        Task[] tasks = new Task[context.ConnectionCount];
        for (int laneIndex = 0; laneIndex < context.ConnectionCount; laneIndex++)
        {
            int capturedLaneIndex = laneIndex;
            tasks[capturedLaneIndex] = laneProcessor.ProcessAsync(
                state,
                context,
                state.Runtime.Reader,
                capturedLaneIndex,
                state.Runtime.ProcessingCancellationToken);
        }

        return tasks;
    }
}
