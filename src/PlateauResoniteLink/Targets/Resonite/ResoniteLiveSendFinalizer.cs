using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSendFinalizer
{
    Task<SceneImportExecutionResult> CompleteAsync(
        LiveSendRunState state,
        LiveSendFinalizationContext context,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteLiveSendFinalizer(
    IResoniteQueuedCityObjectEnqueuer queuedCityObjectEnqueuer) : IResoniteLiveSendFinalizer
{
    private readonly IResoniteQueuedCityObjectEnqueuer queuedCityObjectEnqueuer =
        queuedCityObjectEnqueuer ?? throw new ArgumentNullException(nameof(queuedCityObjectEnqueuer));

    public async Task<SceneImportExecutionResult> CompleteAsync(
        LiveSendRunState state,
        LiveSendFinalizationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        LiveSendExecutionRuntime runtime = state.Runtime;
        LiveSendRunContext runContext = state.Context;
        CompositeCityObjectBaker? cityObjectBaker = runContext.CityObjectBaker;

        if (cityObjectBaker is not null)
        {
            await FlushBufferedCityObjectsAsync(
                state,
                cityObjectBaker,
                context,
                cancellationToken);
        }

        ReportProgress(
            context,
            PlateauLog.Info(
                "live",
                $"Completing live send. Closing lane writers (attempted={state.Progress.AttemptedCityObjectCount}, "
                + $"prepared={state.Progress.ProcessedCityObjectCount}, failed={state.Progress.FailedCityObjectCount})."));
        runtime.CompleteWriter();

        ReportProgress(
            context,
            PlateauLog.Info(
                "live",
                $"Awaiting {runtime.ProcessingTaskCount} send lane task(s) to drain after queue close."));
        await runtime.AwaitCompletionAsync(cancellationToken);
        ReportProgress(context, PlateauLog.Info("live", "All send lanes drained and completion barrier passed."));
        context.Diagnostics.CompleteSendWindow();
        ReportProgress(
            context,
            PlateauLog.Info(
                "live",
                $"Completed {state.Progress.ProcessedCityObjectCount} city objects "
                + $"(failed={state.Progress.FailedCityObjectCount}, attempted={state.Progress.AttemptedCityObjectCount})."));
        ReportProgress(
            context,
            PlateauLog.Info(
                "live",
                $"Send summary: attempted={state.Progress.AttemptedCityObjectCount} sent={state.Progress.ProcessedCityObjectCount} failed={state.Progress.FailedCityObjectCount}."));

        return new SceneImportExecutionResult(
            [$"{context.Endpoint}#{state.Placement.SceneAnchor?.LocationSlot.Value ?? runContext.DatasetRootSlot.Locator.Value}"],
            state.Progress.ProcessedCityObjectCount,
            state.Progress.FailedCityObjectCount,
            CreateDataSourceUsages(state));
    }

    private async Task FlushBufferedCityObjectsAsync(
        LiveSendRunState state,
        CompositeCityObjectBaker cityObjectBaker,
        LiveSendFinalizationContext context,
        CancellationToken cancellationToken)
    {
        (string Name, int InputCount, int OutputCount)[] pendingBakeSummaries = cityObjectBaker
            .GetBakeSummaries()
            .Where(static summary => summary.InputCount > 0)
            .ToArray();
        if (pendingBakeSummaries.Length > 0)
        {
            string summaryText = string.Join(
                ", ",
                pendingBakeSummaries.Select(static summary =>
                    $"{summary.Name}: input={summary.InputCount}, currentOutput={summary.OutputCount}"));
            ReportProgress(context, PlateauLog.Info("live", $"Starting buffered bake flush: {summaryText}."));
        }

        Stopwatch bakeFlushStopwatch = Stopwatch.StartNew();
        int bakedCityObjectCount = await queuedCityObjectEnqueuer.FlushBufferedAsync(
            state,
            cityObjectBaker,
            context.EnqueueContext,
            cancellationToken);
        bakeFlushStopwatch.Stop();
        ReportProgress(
            context,
            PlateauLog.Info(
                "live",
                $"Buffered bake flush produced {bakedCityObjectCount} baked city objects "
                + $"in {bakeFlushStopwatch.Elapsed.TotalSeconds:F3}s."));

        foreach ((string name, int inputCount, int outputCount) in cityObjectBaker.GetBakeSummaries().Where(static summary => summary.OutputCount > 0))
        {
            ReportProgress(
                context,
                PlateauLog.Debug(
                    "live",
                    $"{name} batched {inputCount} input city objects "
                    + $"into {outputCount} baked batch objects."));
        }
    }

    private static ImportDataSourceUsage[] CreateDataSourceUsages(LiveSendRunState state)
    {
        return state.DemSourceUseCounts
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => new ImportDataSourceUsage(
                ImportDataSourceCategory.DemTextureSource,
                pair.Key,
                pair.Value))
            .ToArray();
    }

    private static void ReportProgress(LiveSendFinalizationContext context, string message)
    {
        context.ProgressReporter?.Invoke(message);
    }
}

internal sealed record LiveSendFinalizationContext(
    Uri Endpoint,
    LiveSendEnqueueContext EnqueueContext,
    ResoniteLinkSendDiagnostics Diagnostics,
    Action<string>? ProgressReporter);
