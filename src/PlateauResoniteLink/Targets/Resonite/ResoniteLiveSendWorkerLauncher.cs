using System;
using System.Diagnostics;

using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record LiveSendWorkerLaunchRequest(
    LiveSendRunState State,
    LiveSendQueuePlan QueuePlan,
    ResoniteImportBudgetProfile ResourceBudget);

internal interface IResoniteLiveSendWorkerLauncher
{
    void Launch(
        LiveSendWorkerLaunchRequest request,
        LiveSendRunStartContext context);
}

internal sealed class ResoniteLiveSendWorkerLauncher(
    IResoniteQueuedCityObjectWorker queuedCityObjectWorker) : IResoniteLiveSendWorkerLauncher
{
    private readonly IResoniteQueuedCityObjectWorker queuedCityObjectWorker =
        queuedCityObjectWorker ?? throw new ArgumentNullException(nameof(queuedCityObjectWorker));

    public void Launch(
        LiveSendWorkerLaunchRequest request,
        LiveSendRunStartContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.State);
        ArgumentNullException.ThrowIfNull(request.QueuePlan);
        ArgumentNullException.ThrowIfNull(request.ResourceBudget);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Endpoint);
        ArgumentNullException.ThrowIfNull(context.ClientSession);
        ArgumentNullException.ThrowIfNull(context.Diagnostics);
        int connectionCount = request.QueuePlan.ConnectionCount;
        ArgumentOutOfRangeException.ThrowIfLessThan(connectionCount, 1);

        ReportProgress(
            context,
            PlateauLog.Info(
                "live",
                $"Starting routed send workers (connection_pool={connectionCount})."));
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
                context.ProgressReporter)));
        ReportProgress(
            context,
            PlateauLog.Info(
                "live",
                $"Send lane tasks launched (connection budget={connectionCount}, "
                + $"queue_capacity_total={request.QueuePlan.QueueCapacity}, "
                + $"memory_budget_bytes={request.QueuePlan.MemoryBudgetBytes}, "
                + $"memory_profile={request.ResourceBudget.Name.ToString().ToLowerInvariant()}, "
                + $"runtime_vram_budget_bytes={request.ResourceBudget.RuntimeVramBudgetBytes})."));
        laneStartStopwatch.Stop();
        ReportProgress(
            context,
            PlateauLog.Info(
                "live",
                $"Send workers ready against connection pool={connectionCount}."));
        ReportProgress(
            context,
            PlateauLog.Info(
                "live",
                $"Send lane startup phase complete in {laneStartStopwatch.Elapsed.TotalSeconds:F2}s."));
    }

    private static IResoniteLinkClient GetRoutedClient(LiveSendRunStartContext context)
    {
        return context.ClientSession.GetRequiredClient();
    }

    private static void ReportProgress(
        LiveSendRunStartContext context,
        string message)
    {
        context.ProgressReporter?.Invoke(message);
    }
}
