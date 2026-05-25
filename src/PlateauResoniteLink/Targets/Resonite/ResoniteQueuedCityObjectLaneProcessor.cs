using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Logging;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteQueuedCityObjectLaneProcessor
{
    Task ProcessAsync(
        LiveSendRunState state,
        LiveSendWorkerContext context,
        ChannelReader<LiveSendQueuedCityObject> reader,
        int laneIndex,
        CancellationToken cancellationToken);
}

internal interface IResoniteQueuedCityObjectLaneProcessorFactory
{
    IResoniteQueuedCityObjectLaneProcessor Create(
        IResoniteQueuedCityObjectSender queuedCityObjectSender);
}

internal sealed class ResoniteQueuedCityObjectLaneProcessorFactory : IResoniteQueuedCityObjectLaneProcessorFactory
{
    public IResoniteQueuedCityObjectLaneProcessor Create(
        IResoniteQueuedCityObjectSender queuedCityObjectSender)
    {
        ArgumentNullException.ThrowIfNull(queuedCityObjectSender);

        return new ResoniteQueuedCityObjectLaneProcessor(queuedCityObjectSender);
    }
}

internal sealed class ResoniteQueuedCityObjectLaneProcessor(
    IResoniteQueuedCityObjectSender queuedCityObjectSender) : IResoniteQueuedCityObjectLaneProcessor
{
    private readonly IResoniteQueuedCityObjectSender queuedCityObjectSender =
        queuedCityObjectSender ?? throw new ArgumentNullException(nameof(queuedCityObjectSender));

    public async Task ProcessAsync(
        LiveSendRunState state,
        LiveSendWorkerContext context,
        ChannelReader<LiveSendQueuedCityObject> reader,
        int laneIndex,
        CancellationToken cancellationToken)
    {
        Stopwatch laneClientStopwatch = Stopwatch.StartNew();
        ResoniteSceneSetupInfo setupInfo = state.Context.Plan.SetupInfo;
        if (laneIndex == 0)
        {
            ReportProgress(
                context,
                PlateauLog.Info(
                    "live",
                    $"Send worker {laneIndex + 1}/{context.ConnectionCount} is ready to consume from the routed connection pool."));
        }
        else
        {
            ReportProgress(
                context,
                PlateauLog.Info(
                    "live",
                    $"Preparing send worker {laneIndex + 1}/{context.ConnectionCount} "
                    + $"against routed connections to {context.Endpoint} for dataset '{setupInfo.Dataset}' mesh '{setupInfo.MeshCode}'."));
        }

        laneClientStopwatch.Stop();
        try
        {
            ReportProgress(
                context,
                PlateauLog.Info(
                    "live",
                    $"Send worker {laneIndex + 1}/{context.ConnectionCount} ready against routed connections "
                    + $"in {laneClientStopwatch.Elapsed.TotalSeconds:F2}s."));
            await ProcessQueuedCityObjectsAsync(state, context, reader, laneIndex, cancellationToken);
        }
        catch (Exception exception)
        {
            TryMarkProcessingFailure(state, exception);
            CancelProcessing(state);
            throw;
        }
    }

    private async Task ProcessQueuedCityObjectsAsync(
        LiveSendRunState state,
        LiveSendWorkerContext context,
        ChannelReader<LiveSendQueuedCityObject> reader,
        int laneIndex,
        CancellationToken cancellationToken)
    {
        LiveSendQueuedCityObject? currentCityObject = null;
        try
        {
            if (Interlocked.CompareExchange(ref state.Progress.FirstCityObjectStreamingStartedLogged, 1, 0) == 0)
            {
                ReportProgress(
                    context,
                    PlateauLog.Info(
                        "live",
                        $"City-object send pipeline is active and waiting for queue on lane {laneIndex + 1}/{context.ConnectionCount}."));
            }

            await foreach (LiveSendQueuedCityObject queuedCityObject in reader.ReadAllAsync(cancellationToken))
            {
                currentCityObject = queuedCityObject;
                if (Interlocked.CompareExchange(ref state.Progress.FirstCityObjectDequeuedLogged, 1, 0) == 0)
                {
                    ReportProgress(
                        context,
                        PlateauLog.Info(
                            "live",
                            $"First city object dequeued on lane {laneIndex + 1}/{context.ConnectionCount} "
                            + $"after scene-start {state.Runtime.ElapsedTotalSeconds:F3}s: "
                            + $"{queuedCityObject.CityObject.DisplayName} "
                            + $"({queuedCityObject.CityObject.PackageName}/{queuedCityObject.CityObject.SlotKey})."));
                }

                await queuedCityObjectSender.SendAsync(
                    state,
                    context.GetRoutedClient(),
                    queuedCityObject,
                    context.Diagnostics,
                    context.ProgressReporter,
                    cancellationToken);
                currentCityObject = null;
            }

            ReportProgress(
                context,
                PlateauLog.Info(
                    "live",
                    $"Send lane {laneIndex + 1}/{context.ConnectionCount} drained."));
        }
        catch (OperationCanceledException)
        {
            ReportProgress(
                context,
                PlateauLog.Warning("live", $"Send lane {laneIndex + 1}/{context.ConnectionCount} canceled."));
            throw;
        }
        catch (Exception exception)
        {
            TryMarkProcessingFailure(state, exception);
            CancelProcessing(state);
            string cityObjectContext = currentCityObject is null
                ? string.Empty
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $" while processing '{currentCityObject.CityObject.DisplayName}' "
                    + $"({currentCityObject.CityObject.PackageName}/{currentCityObject.CityObject.SlotKey}) "
                    + $"mesh='{currentCityObject.CityObject.ActualMeshCode}' "
                    + $"sourceFile='{currentCityObject.CityObject.SourceFileRelativePath ?? "<null>"}'");
            ReportProgress(
                context,
                PlateauLog.Error(
                    "live",
                    $"Send lane {laneIndex + 1}/{context.ConnectionCount} failed{cityObjectContext}: {exception.Message}"));
            throw;
        }
    }

    private static void ReportProgress(LiveSendWorkerContext context, string message)
    {
        context.ProgressReporter?.Invoke(message);
    }

    private static void TryMarkProcessingFailure(LiveSendRunState state, Exception exception)
    {
        state.Runtime.TryMarkFailure(exception);
    }

    private static void CancelProcessing(LiveSendRunState state)
    {
        state.Runtime.Cancel();
    }
}
