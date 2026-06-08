using System;
using System.Diagnostics;

using PlateauResoniteLink.Diagnostics;

using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record LiveSendWorkerLaunchRequest
{
    public LiveSendWorkerLaunchRequest(
        LiveSendRunState State,
        LiveSendQueuePlan QueuePlan,
        ResoniteImportBudgetProfile ResourceBudget)
    {
        ArgumentNullException.ThrowIfNull(State);
        ArgumentNullException.ThrowIfNull(QueuePlan);
        ArgumentNullException.ThrowIfNull(ResourceBudget);

        this.State = State;
        this.QueuePlan = QueuePlan;
        this.ResourceBudget = ResourceBudget;
    }

    public LiveSendRunState State { get; }

    public LiveSendQueuePlan QueuePlan { get; }

    public ResoniteImportBudgetProfile ResourceBudget { get; }
}

internal sealed class ResoniteLiveSendWorkerLauncher(
    ResoniteQueuedCityObjectWorker queuedCityObjectWorker)
{
    private readonly ResoniteQueuedCityObjectWorker queuedCityObjectWorker =
        queuedCityObjectWorker ?? throw new ArgumentNullException(nameof(queuedCityObjectWorker));

    public void Launch(
        LiveSendWorkerLaunchRequest request,
        LiveSendRunStartContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        int connectionCount = request.QueuePlan.ConnectionCount;

        context.Logger.WriteInformation(
            "Starting routed send workers (connection_pool={ConnectionCount}).",
            connectionCount);
        request.State.Progress.Reset();
        Stopwatch laneStartStopwatch = Stopwatch.StartNew();
        context.Diagnostics.StartSendWindow(connectionCount);
        request.State.Runtime.Start(queuedCityObjectWorker.CreateProcessingTasks(
            request.State,
            new LiveSendWorkerContext(
                context.Endpoint,
                connectionCount,
                () => GetRoutedClient(context),
                context.Diagnostics,
                context.Logger)));
        context.Logger.WriteInformation(
            "Send lane tasks launched (connection_budget={ConnectionCount}, queue_capacity_total={QueueCapacity}, memory_budget_bytes={MemoryBudgetBytes}, memory_profile={MemoryProfile}, runtime_vram_budget_bytes={RuntimeVramBudgetBytes}).",
            connectionCount,
            request.QueuePlan.QueueCapacity,
            request.QueuePlan.MemoryBudgetBytes,
            request.ResourceBudget.Name.ToString().ToLowerInvariant(),
            request.ResourceBudget.RuntimeVramBudgetBytes);
        laneStartStopwatch.Stop();
        context.Logger.WriteInformation(
            "Send workers ready against connection pool={ConnectionCount}.",
            connectionCount);
        context.Logger.WriteInformation(
            "Send lane startup phase complete in {ElapsedSeconds:F2}s.",
            laneStartStopwatch.Elapsed.TotalSeconds);
    }

    private static IResoniteLinkClient GetRoutedClient(LiveSendRunStartContext context)
    {
        return context.ClientSession.GetRequiredClient();
    }

}
