using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using PlateauResoniteLink.Diagnostics;

using PlateauResoniteLink.Application.Importing;
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
        await FlushBufferedCityObjectsAsync(
            state,
            runContext.CityObjectBaker,
            context,
            cancellationToken);

        context.Logger.WriteInformation(
            "Completing live send. Closing lane writers (attempted={AttemptedCount}, processed={ProcessedCount}, failed={FailedCount}, queued_source={QueuedSourceCount}).",
            state.Progress.AttemptedCityObjectCount,
            state.Progress.ProcessedCityObjectCount,
            state.Progress.FailedCityObjectCount,
            state.Progress.QueuedCityObjectCount);
        runtime.CompleteWriter();

        context.Logger.WriteInformation(
            "Awaiting {ProcessingTaskCount} send lane task(s) to drain after queue close.",
            runtime.ProcessingTaskCount);
        await runtime.AwaitCompletionAsync(cancellationToken);
        context.Logger.WriteInformation("All send lanes drained and completion barrier passed.");
        context.Diagnostics.CompleteSendWindow();
        context.Logger.WriteInformation(
            "Completed {ProcessedCount} city objects (failed={FailedCount}, attempted={AttemptedCount}, queued_source={QueuedSourceCount}).",
            state.Progress.ProcessedCityObjectCount,
            state.Progress.FailedCityObjectCount,
            state.Progress.AttemptedCityObjectCount,
            state.Progress.QueuedCityObjectCount);
        context.Logger.WriteInformation(
            "Send summary: attempted={AttemptedCount} sent={SentCount} failed={FailedCount} queued_source={QueuedSourceCount}.",
            state.Progress.AttemptedCityObjectCount,
            state.Progress.ProcessedCityObjectCount,
            state.Progress.FailedCityObjectCount,
            state.Progress.QueuedCityObjectCount);

        return new SceneImportExecutionResult(
            [$"{context.Endpoint}#{state.Placement.SceneAnchor?.LocationSlot.Value ?? runContext.DatasetRootSlot.Locator.Value}"],
            state.Progress.ProcessedCityObjectCount,
            state.Progress.FailedCityObjectCount,
            CreateDataSourceUsages(state));
    }

    private static async Task FlushBufferedCityObjectsAsync(
        LiveSendRunState state,
        NonDemCityObjectBaker cityObjectBaker,
        LiveSendFinalizationContext context,
        CancellationToken cancellationToken)
    {
        (string Name, int InputCount, int OutputCount) bakeSummary = cityObjectBaker.GetBakeSummary();
        (string Name, int InputCount, int OutputCount)[] pendingBakeSummaries = bakeSummary.InputCount > 0
            ? [bakeSummary]
            : [];
        if (pendingBakeSummaries.Length > 0)
        {
            string summaryText = string.Join(
                ", ",
                pendingBakeSummaries.Select(static summary =>
                    $"{summary.Name}: input={summary.InputCount}, currentOutput={summary.OutputCount}"));
            context.Logger.WriteInformation("Starting buffered bake flush: {SummaryText}.", summaryText);
        }

        Stopwatch bakeFlushStopwatch = Stopwatch.StartNew();
        int bakedCityObjectCount = await ResoniteQueuedCityObjectEnqueuer.FlushBufferedAsync(
            state,
            cityObjectBaker,
            context.EnqueueContext,
            cancellationToken);
        bakeFlushStopwatch.Stop();
        context.Logger.WriteInformation(
            "Buffered bake flush produced {BakedCityObjectCount} baked city objects in {ElapsedSeconds:F3}s.",
            bakedCityObjectCount,
            bakeFlushStopwatch.Elapsed.TotalSeconds);

        bakeSummary = cityObjectBaker.GetBakeSummary();
        if (bakeSummary.OutputCount > 0)
        {
            context.Logger.WriteDebug(
                "{Name} batched {InputCount} input city objects into {OutputCount} baked batch objects.",
                bakeSummary.Name,
                bakeSummary.InputCount,
                bakeSummary.OutputCount);
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

}

internal sealed record LiveSendFinalizationContext
{
    public LiveSendFinalizationContext(
        Uri Endpoint,
        LiveSendEnqueueContext EnqueueContext,
        ResoniteLinkSendDiagnostics Diagnostics,
        ILogger Logger)
    {
        ArgumentNullException.ThrowIfNull(Endpoint);
        ArgumentNullException.ThrowIfNull(EnqueueContext);
        ArgumentNullException.ThrowIfNull(Diagnostics);

        this.Endpoint = Endpoint;
        this.EnqueueContext = EnqueueContext;
        this.Diagnostics = Diagnostics;
        this.Logger = Logger;
    }

    public Uri Endpoint { get; }

    public LiveSendEnqueueContext EnqueueContext { get; }

    public ResoniteLinkSendDiagnostics Diagnostics { get; }

    public ILogger Logger { get; }
}
