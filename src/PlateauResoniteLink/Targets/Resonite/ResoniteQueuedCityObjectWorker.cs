using System;
using System.Globalization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;

using Microsoft.Extensions.Logging;

using PlateauResoniteLink.Diagnostics;

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
                context.Logger.WriteInformation(
                    "City-object send pipeline is active and waiting for queue on lane {LaneIndex}/{ConnectionCount}.",
                    laneIndex + 1,
                    context.ConnectionCount);
            }

            await foreach (LiveSendQueuedCityObject queuedCityObject in reader.ReadAllAsync(cancellationToken))
            {
                currentCityObject = queuedCityObject;
                if (Interlocked.CompareExchange(ref state.Progress.FirstCityObjectDequeuedLogged, 1, 0) == 0)
                {
                    context.Logger.WriteInformation(
                        "First city object dequeued on lane {LaneIndex}/{ConnectionCount} after scene-start {ElapsedSeconds:F3}s: {DisplayName} ({PackageName}/{SlotKey}).",
                        laneIndex + 1,
                        context.ConnectionCount,
                        state.Runtime.ElapsedTotalSeconds,
                        queuedCityObject.CityObject.DisplayName,
                        queuedCityObject.CityObject.PackageName,
                        queuedCityObject.CityObject.SlotKey);
                }

                await SendQueuedCityObjectAsync(
                    state,
                    context.GetRoutedClient(),
                    queuedCityObject,
                    context.Diagnostics,
                    context.Logger,
                    cancellationToken);
                currentCityObject = null;
            }

            context.Logger.WriteInformation(
                "Send lane {LaneIndex}/{ConnectionCount} drained.",
                laneIndex + 1,
                context.ConnectionCount);
        }
        catch (OperationCanceledException)
        {
            context.Logger.WriteWarning(
                "Send lane {LaneIndex}/{ConnectionCount} canceled.",
                laneIndex + 1,
                context.ConnectionCount);
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
            context.Logger.WriteError(
                exception,
                "Send lane {LaneIndex}/{ConnectionCount} failed{CityObjectContext}: {Reason}",
                laneIndex + 1,
                context.ConnectionCount,
                cityObjectContext,
                exception.Message);
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
            context.Logger.WriteInformation(
                "Send worker {LaneIndex}/{ConnectionCount} is ready to consume from the routed connection pool.",
                laneIndex + 1,
                context.ConnectionCount);
        }
        else
        {
            context.Logger.WriteInformation(
                "Preparing send worker {LaneIndex}/{ConnectionCount} against routed connections to {Endpoint} for dataset '{Dataset}' mesh '{MeshCode}'.",
                laneIndex + 1,
                context.ConnectionCount,
                context.Endpoint,
                setupInfo.Dataset,
                setupInfo.MeshCode);
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
        ILogger logger,
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
                logger,
                cancellationToken);
            await preparedCityObjectImporter.ImportAsync(
                state,
                routedClient,
                queuedCityObject,
                preparedCityObject,
                diagnostics,
                logger,
                cancellationToken);

            int processedCount = Interlocked.Increment(ref state.Progress.ProcessedCityObjectCount);
            logger.WriteDebug(
                "Sent city object {ProcessedCount}: {DisplayName} ({PackageName}/{SlotKey})",
                processedCount,
                preparedCityObject.CityObject.DisplayName,
                preparedCityObject.CityObject.PackageName,
                preparedCityObject.CityObject.SlotKey);
            if (processedCount % 25 == 0)
            {
                logger.WriteInformation(
                    "Live send progress: attempted={AttemptedCount}, sent={SentCount}, failed={FailedCount}, queued_source={QueuedSourceCount}.",
                    state.Progress.AttemptedCityObjectCount,
                    processedCount,
                    state.Progress.FailedCityObjectCount,
                    state.Progress.QueuedCityObjectCount);
            }
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
            logger.WriteWarning(
                "Skipping city object after send failure {FailedCount}: {DisplayName} ({PackageName}/{SlotKey}). Reason: {Reason}",
                failedCount,
                queuedCityObject.CityObject.DisplayName,
                queuedCityObject.CityObject.PackageName,
                queuedCityObject.CityObject.SlotKey,
                exception.Message);
        }
        finally
        {
            await queuedCityObject.MemoryLease.DisposeAsync();
        }
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
        ILogger Logger)
    {
        ArgumentNullException.ThrowIfNull(Endpoint);
        ArgumentOutOfRangeException.ThrowIfLessThan(ConnectionCount, 1);
        ArgumentNullException.ThrowIfNull(GetRoutedClient);
        ArgumentNullException.ThrowIfNull(Diagnostics);

        this.Endpoint = Endpoint;
        this.ConnectionCount = ConnectionCount;
        this.GetRoutedClient = GetRoutedClient;
        this.Diagnostics = Diagnostics;
        this.Logger = Logger;
    }

    public Uri Endpoint { get; }

    public int ConnectionCount { get; }

    public Func<IResoniteLinkClient> GetRoutedClient { get; }

    public ResoniteLinkSendDiagnostics Diagnostics { get; }

    public ILogger Logger { get; }
}
