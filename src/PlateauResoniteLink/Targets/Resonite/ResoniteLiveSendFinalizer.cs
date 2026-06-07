using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class ResoniteLiveSendFinalizer
{
    public static async Task<SceneImportExecutionResult> CompleteAsync(
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
                + $"processed={state.Progress.ProcessedCityObjectCount}, failed={state.Progress.FailedCityObjectCount})."));
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

    private static async Task FlushBufferedCityObjectsAsync(
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
        int bakedCityObjectCount = await ResoniteQueuedCityObjectEnqueuer.FlushBufferedAsync(
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
            .OrderBy(static pair => DescribeTerrainTextureSource(pair.Key), StringComparer.Ordinal)
            .Select(static pair => new ImportDataSourceUsage(
                ImportDataSourceCategory.DemTextureSource,
                DescribeTerrainTextureSource(pair.Key),
                pair.Value))
            .ToArray();
    }

    private static string DescribeTerrainTextureSource(TerrainTextureSource source)
    {
        return source.Description;
    }

    private static void ReportProgress(LiveSendFinalizationContext context, string message)
    {
        context.ProgressReporter?.Invoke(message);
    }
}

internal sealed record LiveSendFinalizationContext
{
    public LiveSendFinalizationContext(
        Uri Endpoint,
        LiveSendEnqueueContext EnqueueContext,
        ResoniteLinkSendDiagnostics Diagnostics,
        Action<string>? ProgressReporter)
    {
        ArgumentNullException.ThrowIfNull(Endpoint);
        ArgumentNullException.ThrowIfNull(EnqueueContext);
        ArgumentNullException.ThrowIfNull(Diagnostics);

        this.Endpoint = Endpoint;
        this.EnqueueContext = EnqueueContext;
        this.Diagnostics = Diagnostics;
        this.ProgressReporter = ProgressReporter;
    }

    public Uri Endpoint { get; }

    public LiveSendEnqueueContext EnqueueContext { get; }

    public ResoniteLinkSendDiagnostics Diagnostics { get; }

    public Action<string>? ProgressReporter { get; }
}
