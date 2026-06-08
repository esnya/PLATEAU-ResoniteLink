using System;
using System.Globalization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using PlateauResoniteLink.Diagnostics;

using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteQueuedCityObjectWorker
{
    Task[] CreateProcessingTasks(
        LiveSendRunState state,
        LiveSendWorkerContext context);
}

internal sealed class ResoniteQueuedCityObjectWorker(
    IResoniteQueuedCityObjectSender queuedCityObjectSender) : IResoniteQueuedCityObjectWorker
{
    private readonly IResoniteQueuedCityObjectSender queuedCityObjectSender =
        queuedCityObjectSender ?? throw new ArgumentNullException(nameof(queuedCityObjectSender));

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

                await queuedCityObjectSender.SendAsync(
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
