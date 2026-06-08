using System;
using System.Globalization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class ResoniteQueuedCityObjectWorker(
    ResoniteQueuedCityObjectPreparation cityObjectPreparation,
    ResonitePreparedCityObjectImporter preparedCityObjectImporter)
{
    private readonly ResoniteQueuedCityObjectPreparation cityObjectPreparation =
        cityObjectPreparation ?? throw new ArgumentNullException(nameof(cityObjectPreparation));
    private readonly ResonitePreparedCityObjectImporter preparedCityObjectImporter =
        preparedCityObjectImporter ?? throw new ArgumentNullException(nameof(preparedCityObjectImporter));

    public Task[] CreateProcessingTasks(
        LiveSendRunState state,
        LiveSendWorkerContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        Task[] tasks = new Task[context.ConnectionCount];
        for (int laneIndex = 0; laneIndex < context.ConnectionCount; laneIndex++)
        {
            int capturedLaneIndex = laneIndex;
            tasks[capturedLaneIndex] = ProcessQueuedCityObjectsOnLaneAsync(
                state,
                context,
                state.Runtime.Reader,
                capturedLaneIndex,
                state.Runtime.ProcessingCancellationToken);
        }

        return tasks;
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

                await SendQueuedCityObjectAsync(
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

    private async Task ProcessQueuedCityObjectsOnLaneAsync(
        LiveSendRunState state,
        LiveSendWorkerContext context,
        ChannelReader<LiveSendQueuedCityObject> reader,
        int laneIndex,
        CancellationToken cancellationToken)
    {
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

        await ProcessQueuedCityObjectsAsync(state, context, reader, laneIndex, cancellationToken);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Live send should log and skip individual city object send failures while keeping the lane alive.")]
    private async Task SendQueuedCityObjectAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        LiveSendQueuedCityObject queuedCityObject,
        ResoniteLinkSendDiagnostics diagnostics,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref state.Progress.AttemptedCityObjectCount);
        try
        {
            PreparedCityObject preparedCityObject = await cityObjectPreparation.PrepareAsync(
                state,
                routedClient,
                queuedCityObject.CityObject,
                diagnostics,
                progressReporter,
                cancellationToken);
            await preparedCityObjectImporter.ImportAsync(
                state,
                routedClient,
                queuedCityObject,
                preparedCityObject,
                diagnostics,
                progressReporter,
                cancellationToken);

            int processedCount = Interlocked.Increment(ref state.Progress.ProcessedCityObjectCount);
            progressReporter?.Invoke(
                PlateauLog.Info(
                    "live",
                    $"Sent city object {processedCount}: "
                    + $"{preparedCityObject.CityObject.DisplayName} "
                    + $"({preparedCityObject.CityObject.PackageName}/{preparedCityObject.CityObject.SlotKey})"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (!IsRecoverableCityObjectSendFailure(exception))
            {
                throw;
            }

            int failedCount = Interlocked.Increment(ref state.Progress.FailedCityObjectCount);
            progressReporter?.Invoke(
                PlateauLog.Warning(
                    "live",
                    $"Skipping city object after send failure {failedCount}: "
                    + $"{queuedCityObject.CityObject.DisplayName} "
                    + $"({queuedCityObject.CityObject.PackageName}/{queuedCityObject.CityObject.SlotKey}). "
                    + $"Reason: {exception.Message}"));
        }
        finally
        {
            await queuedCityObject.MemoryLease.DisposeAsync();
        }
    }

    private static void ReportProgress(LiveSendWorkerContext context, string message)
    {
        context.ProgressReporter?.Invoke(message);
    }

    private static bool IsRecoverableCityObjectSendFailure(Exception exception)
    {
        return exception is ContinuableImportException
            || FindResoniteLinkOperationException(exception) is { OperationName: "ImportMesh" or "ImportTexture" or "GetSlot" or "GetComponent" };
    }

    private static ResoniteLinkOperationException? FindResoniteLinkOperationException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is ResoniteLinkOperationException operationException)
            {
                return operationException;
            }
        }

        return null;
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

internal sealed record LiveSendWorkerContext
{
    public LiveSendWorkerContext(
        Uri Endpoint,
        int ConnectionCount,
        Func<IResoniteLinkClient> GetRoutedClient,
        ResoniteLinkSendDiagnostics Diagnostics,
        Action<string>? ProgressReporter)
    {
        ArgumentNullException.ThrowIfNull(Endpoint);
        ArgumentOutOfRangeException.ThrowIfLessThan(ConnectionCount, 1);
        ArgumentNullException.ThrowIfNull(GetRoutedClient);
        ArgumentNullException.ThrowIfNull(Diagnostics);

        this.Endpoint = Endpoint;
        this.ConnectionCount = ConnectionCount;
        this.GetRoutedClient = GetRoutedClient;
        this.Diagnostics = Diagnostics;
        this.ProgressReporter = ProgressReporter;
    }

    public Uri Endpoint { get; }

    public int ConnectionCount { get; }

    public Func<IResoniteLinkClient> GetRoutedClient { get; }

    public ResoniteLinkSendDiagnostics Diagnostics { get; }

    public Action<string>? ProgressReporter { get; }
}
