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

internal sealed class ResoniteLiveSendFinalizer(
    IResoniteQueuedCityObjectEnqueuer queuedCityObjectEnqueuer)
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
            context.Logger.WriteInformation("Starting buffered bake flush: {SummaryText}.", summaryText);
        }

        Stopwatch bakeFlushStopwatch = Stopwatch.StartNew();
        int bakedCityObjectCount = await queuedCityObjectEnqueuer.FlushBufferedAsync(
            state,
            cityObjectBaker,
            context.EnqueueContext,
            cancellationToken);
        bakeFlushStopwatch.Stop();
        context.Logger.WriteInformation(
            "Buffered bake flush produced {BakedCityObjectCount} baked city objects in {ElapsedSeconds:F3}s.",
            bakedCityObjectCount,
            bakeFlushStopwatch.Elapsed.TotalSeconds);

        foreach ((string name, int inputCount, int outputCount) in cityObjectBaker.GetBakeSummaries().Where(static summary => summary.OutputCount > 0))
        {
            context.Logger.WriteDebug(
                "{Name} batched {InputCount} input city objects into {OutputCount} baked batch objects.",
                name,
                inputCount,
                outputCount);
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
