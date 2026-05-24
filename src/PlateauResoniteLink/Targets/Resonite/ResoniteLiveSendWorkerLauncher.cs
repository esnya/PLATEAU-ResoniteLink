using System;
using System.Diagnostics;

using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSendWorkerLauncher
{
    void Start(
        LiveSendRunState state,
        int connectionCount,
        Uri endpoint,
        Action<string>? progressReporter,
        ResoniteQueuedCityObjectProcessor processQueuedCityObjectAsync,
        ResoniteLinkSendDiagnostics diagnostics);
}

internal sealed class ResoniteLiveSendWorkerLauncher(
    IResoniteCityObjectSendWorkerPool cityObjectSendWorkerPool) : IResoniteLiveSendWorkerLauncher
{
    private readonly IResoniteCityObjectSendWorkerPool cityObjectSendWorkerPool =
        cityObjectSendWorkerPool ?? throw new ArgumentNullException(nameof(cityObjectSendWorkerPool));

    public void Start(
        LiveSendRunState state,
        int connectionCount,
        Uri endpoint,
        Action<string>? progressReporter,
        ResoniteQueuedCityObjectProcessor processQueuedCityObjectAsync,
        ResoniteLinkSendDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(processQueuedCityObjectAsync);
        ArgumentNullException.ThrowIfNull(diagnostics);

        LiveSendQueuePlan runtimePlan = state.Context.Plan.Queue;
        ResoniteImportBudgetProfile resourceBudget = state.Context.Plan.ResourceBudget;
        LiveSendExecutionRuntime runtime = state.Runtime;
        ReportProgress(
            progressReporter,
            PlateauLog.Info(
                "live",
                $"Starting routed send workers (connection_pool={connectionCount})."));
        state.Progress.Reset();
        Stopwatch laneStartStopwatch = Stopwatch.StartNew();
        diagnostics.StartSendWindow(connectionCount);
        runtime.Start(cityObjectSendWorkerPool.CreateProcessingTasks(
            state,
            runtime,
            connectionCount,
            endpoint,
            progressReporter,
            processQueuedCityObjectAsync));
        ReportProgress(
            progressReporter,
            PlateauLog.Info(
                "live",
                $"Send lane tasks launched (connection budget={connectionCount}, "
                + $"queue_capacity_total={runtimePlan.QueueCapacity}, "
                + $"memory_budget_bytes={runtimePlan.MemoryBudgetBytes}, "
                + $"memory_profile={resourceBudget.Name.ToString().ToLowerInvariant()}, "
                + $"runtime_vram_budget_bytes={resourceBudget.RuntimeVramBudgetBytes})."));
        laneStartStopwatch.Stop();
        ReportProgress(
            progressReporter,
            PlateauLog.Info(
                "live",
                $"Send workers ready against connection pool={connectionCount}."));
        ReportProgress(
            progressReporter,
            PlateauLog.Info(
                "live",
                $"Send lane startup phase complete in {laneStartStopwatch.Elapsed.TotalSeconds:F2}s."));
    }

    private static void ReportProgress(Action<string>? progressReporter, string message)
    {
        progressReporter?.Invoke(message);
    }
}
