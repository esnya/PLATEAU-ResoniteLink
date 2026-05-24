using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSendRunFinalizer
{
    Task<IReadOnlyList<string>> CompleteAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        Uri endpoint,
        int connectionCount,
        ResoniteLinkSendDiagnostics diagnostics,
        Action<string>? progressReporter,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteLiveSendRunFinalizer(
    IResoniteCityObjectQueueWriter cityObjectQueueWriter) : IResoniteLiveSendRunFinalizer
{
    private readonly IResoniteCityObjectQueueWriter cityObjectQueueWriter =
        cityObjectQueueWriter ?? throw new ArgumentNullException(nameof(cityObjectQueueWriter));

    public async Task<IReadOnlyList<string>> CompleteAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        Uri endpoint,
        int connectionCount,
        ResoniteLinkSendDiagnostics diagnostics,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(routedClient);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(diagnostics);

        LiveSendExecutionRuntime runtime = state.Runtime;
        LiveSendRunContext context = state.Context;
        CompositeCityObjectBaker? cityObjectBaker = context.CityObjectBaker;

        if (cityObjectBaker is not null)
        {
            await FlushBufferedCityObjectsAsync(
                state,
                routedClient,
                connectionCount,
                cityObjectBaker,
                progressReporter,
                cancellationToken);
        }

        ReportProgress(
            progressReporter,
            PlateauLog.Info(
                "live",
                $"Completing live send. Closing lane writers (attempted={state.Progress.AttemptedCityObjectCount}, "
                + $"prepared={state.Progress.ProcessedCityObjectCount}, failed={state.Progress.FailedCityObjectCount})."));
        runtime.CompleteWriter();

        ReportProgress(
            progressReporter,
            PlateauLog.Info(
                "live",
                $"Awaiting {runtime.ProcessingTaskCount} send lane task(s) to drain after queue close."));
        await runtime.AwaitCompletionAsync(cancellationToken);
        ReportProgress(progressReporter, PlateauLog.Info("live", "All send lanes drained and completion barrier passed."));
        diagnostics.CompleteSendWindow();
        ReportProgress(
            progressReporter,
            PlateauLog.Info(
                "live",
                $"Completed {state.Progress.ProcessedCityObjectCount} city objects "
                + $"(failed={state.Progress.FailedCityObjectCount}, attempted={state.Progress.AttemptedCityObjectCount})."));
        ReportProgress(
            progressReporter,
            PlateauLog.Info(
                "live",
                $"Send summary: attempted={state.Progress.AttemptedCityObjectCount} sent={state.Progress.ProcessedCityObjectCount} failed={state.Progress.FailedCityObjectCount}."));

        return [$"{endpoint}#{state.Placement.SceneAnchor?.LocationSlot.Value ?? context.DatasetRootSlot.Locator.Value}"];
    }

    private async Task FlushBufferedCityObjectsAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        int connectionCount,
        CompositeCityObjectBaker cityObjectBaker,
        Action<string>? progressReporter,
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
            ReportProgress(progressReporter, PlateauLog.Info("live", $"Starting buffered bake flush: {summaryText}."));
        }

        Stopwatch bakeFlushStopwatch = Stopwatch.StartNew();
        int bakedCityObjectCount = await cityObjectQueueWriter.FlushBufferedCityObjectsAsync(
            state,
            cityObjectBaker,
            routedClient,
            connectionCount,
            progressReporter,
            cancellationToken);
        bakeFlushStopwatch.Stop();
        ReportProgress(
            progressReporter,
            PlateauLog.Info(
                "live",
                $"Buffered bake flush produced {bakedCityObjectCount} baked city objects "
                + $"in {bakeFlushStopwatch.Elapsed.TotalSeconds:F3}s."));

        foreach ((string name, int inputCount, int outputCount) in cityObjectBaker.GetBakeSummaries().Where(static summary => summary.OutputCount > 0))
        {
            ReportProgress(
                progressReporter,
                PlateauLog.Debug(
                    "live",
                    $"{name} batched {inputCount} input city objects "
                    + $"into {outputCount} baked batch objects."));
        }
    }

    private static void ReportProgress(Action<string>? progressReporter, string message)
    {
        progressReporter?.Invoke(message);
    }
}
