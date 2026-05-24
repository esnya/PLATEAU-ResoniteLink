using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Logging;

namespace PlateauResoniteLink.Targets.Resonite;

internal delegate Task ResoniteQueuedCityObjectProcessor(
    LiveSendRunState state,
    QueuedCityObject queuedCityObject,
    CancellationToken cancellationToken);

internal interface IResoniteCityObjectSendWorkerPool
{
    Task[] CreateProcessingTasks(
        LiveSendRunState state,
        LiveSendExecutionRuntime runtime,
        int connectionCount,
        Uri endpoint,
        Action<string>? progressReporter,
        ResoniteQueuedCityObjectProcessor processQueuedCityObjectAsync);
}

internal sealed class ResoniteCityObjectSendWorkerPool : IResoniteCityObjectSendWorkerPool
{
    public Task[] CreateProcessingTasks(
        LiveSendRunState state,
        LiveSendExecutionRuntime runtime,
        int connectionCount,
        Uri endpoint,
        Action<string>? progressReporter,
        ResoniteQueuedCityObjectProcessor processQueuedCityObjectAsync)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(processQueuedCityObjectAsync);

        Task[] tasks = new Task[connectionCount];
        for (int laneIndex = 0; laneIndex < connectionCount; laneIndex++)
        {
            int capturedLaneIndex = laneIndex;
            tasks[capturedLaneIndex] = ProcessQueuedCityObjectsOnLaneAsync(
                state,
                runtime.Reader,
                capturedLaneIndex,
                connectionCount,
                endpoint,
                progressReporter,
                processQueuedCityObjectAsync,
                runtime.ProcessingCancellationToken);
        }

        return tasks;
    }

    private static async Task ProcessQueuedCityObjectsAsync(
        LiveSendRunState state,
        ChannelReader<QueuedCityObject> reader,
        int laneIndex,
        int connectionCount,
        Action<string>? progressReporter,
        ResoniteQueuedCityObjectProcessor processQueuedCityObjectAsync,
        CancellationToken cancellationToken)
    {
        QueuedCityObject? currentCityObject = null;
        try
        {
            if (Interlocked.CompareExchange(ref state.Progress.FirstCityObjectStreamingStartedLogged, 1, 0) == 0)
            {
                progressReporter?.Invoke(
                    PlateauLog.Info(
                        "live",
                        $"City-object send pipeline is active and waiting for queue on lane {laneIndex + 1}/{connectionCount}."));
            }

            await foreach (QueuedCityObject queuedCityObject in reader.ReadAllAsync(cancellationToken))
            {
                currentCityObject = queuedCityObject;
                if (Interlocked.CompareExchange(ref state.Progress.FirstCityObjectDequeuedLogged, 1, 0) == 0)
                {
                    progressReporter?.Invoke(
                        PlateauLog.Info(
                            "live",
                            $"First city object dequeued on lane {laneIndex + 1}/{connectionCount} "
                            + $"after scene-start {state.Runtime.ElapsedTotalSeconds:F3}s: "
                            + $"{queuedCityObject.CityObject.DisplayName} "
                            + $"({queuedCityObject.CityObject.PackageName}/{queuedCityObject.CityObject.SlotKey})."));
                }

                await processQueuedCityObjectAsync(state, queuedCityObject, cancellationToken);
                currentCityObject = null;
            }

            progressReporter?.Invoke(
                PlateauLog.Info(
                    "live",
                    $"Send lane {laneIndex + 1}/{connectionCount} drained."));
        }
        catch (OperationCanceledException)
        {
            progressReporter?.Invoke(PlateauLog.Warning("live", $"Send lane {laneIndex + 1}/{connectionCount} canceled."));
            throw;
        }
        catch (Exception exception)
        {
            MarkProcessingFailure(state, exception);
            CancelProcessing(state);
            string cityObjectContext = currentCityObject is null
                ? string.Empty
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $" while processing '{currentCityObject.CityObject.DisplayName}' "
                    + $"({currentCityObject.CityObject.PackageName}/{currentCityObject.CityObject.SlotKey}) "
                    + $"mesh='{currentCityObject.CityObject.ActualMeshCode}' "
                    + $"sourceFile='{currentCityObject.CityObject.SourceFileRelativePath ?? "<null>"}'");
            progressReporter?.Invoke(PlateauLog.Error("live", $"Send lane {laneIndex + 1}/{connectionCount} failed{cityObjectContext}: {exception.Message}"));
            throw;
        }
    }

    private static async Task ProcessQueuedCityObjectsOnLaneAsync(
        LiveSendRunState state,
        ChannelReader<QueuedCityObject> reader,
        int laneIndex,
        int connectionCount,
        Uri endpoint,
        Action<string>? progressReporter,
        ResoniteQueuedCityObjectProcessor processQueuedCityObjectAsync,
        CancellationToken cancellationToken)
    {
        Stopwatch laneClientStopwatch = Stopwatch.StartNew();
        ResoniteSceneSetupInfo setupInfo = state.Context.Plan.SetupInfo;
        if (laneIndex == 0)
        {
            progressReporter?.Invoke(
                PlateauLog.Info(
                    "live",
                    $"Send worker {laneIndex + 1}/{connectionCount} is ready to consume from the routed connection pool."));
        }
        else
        {
            progressReporter?.Invoke(
                PlateauLog.Info(
                    "live",
                    $"Preparing send worker {laneIndex + 1}/{connectionCount} "
                    + $"against routed connections to {endpoint} for dataset '{setupInfo.Dataset}' mesh '{setupInfo.MeshCode}'."));
        }

        laneClientStopwatch.Stop();
        try
        {
            progressReporter?.Invoke(
                PlateauLog.Info(
                    "live",
                    $"Send worker {laneIndex + 1}/{connectionCount} ready against routed connections "
                    + $"in {laneClientStopwatch.Elapsed.TotalSeconds:F2}s."));
            await ProcessQueuedCityObjectsAsync(
                state,
                reader,
                laneIndex,
                connectionCount,
                progressReporter,
                processQueuedCityObjectAsync,
                cancellationToken);
        }
        catch (Exception exception)
        {
            MarkProcessingFailure(state, exception);
            CancelProcessing(state);
            throw;
        }
    }

    private static void MarkProcessingFailure(LiveSendRunState state, Exception exception)
    {
        state.Runtime.TryMarkFailure(exception);
    }

    private static void CancelProcessing(LiveSendRunState state)
    {
        state.Runtime.Cancel();
    }
}
