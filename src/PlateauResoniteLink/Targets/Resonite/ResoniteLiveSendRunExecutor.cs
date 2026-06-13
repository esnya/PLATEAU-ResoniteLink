using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;


using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Diagnostics;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record LiveSendRunExecutionContext
{
    public LiveSendRunExecutionContext(
        Uri Endpoint,
        int ConnectionCount,
        ILiveSendClientSession ClientSession,
        ResoniteLinkSendDiagnostics Diagnostics)
    {
        ArgumentNullException.ThrowIfNull(Endpoint);
        ArgumentOutOfRangeException.ThrowIfLessThan(ConnectionCount, 1);
        ArgumentNullException.ThrowIfNull(ClientSession);
        ArgumentNullException.ThrowIfNull(Diagnostics);

        this.Endpoint = Endpoint;
        this.ConnectionCount = ConnectionCount;
        this.ClientSession = ClientSession;
        this.Diagnostics = Diagnostics;
    }

    public Uri Endpoint { get; }

    public int ConnectionCount { get; }

    public ILiveSendClientSession ClientSession { get; }

    public ResoniteLinkSendDiagnostics Diagnostics { get; }
}

internal interface IResoniteLiveSendRunExecutorFactory
{
    IResoniteLiveSendRunExecutor Create(ResoniteLiveSendRunStarter runStarter);
}

internal sealed class ResoniteLiveSendRunExecutorFactory : IResoniteLiveSendRunExecutorFactory
{
    public IResoniteLiveSendRunExecutor Create(ResoniteLiveSendRunStarter runStarter)
    {
        ArgumentNullException.ThrowIfNull(runStarter);

        return new ResoniteLiveSendRunExecutor(runStarter);
    }
}

internal interface IResoniteLiveSendRunExecutor
{
    Task<SceneImportExecutionResult> ExecuteAsync(
        LiveSendRunStartRequest request,
        IAsyncEnumerable<ImportedObjectUnit> objectUnits,
        LiveSendRunExecutionContext context,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteLiveSendRunExecutor(
    ResoniteLiveSendRunStarter runStarter) : IResoniteLiveSendRunExecutor
{
    private readonly ResoniteLiveSendRunStarter runStarter =
        runStarter ?? throw new ArgumentNullException(nameof(runStarter));

    public async Task<SceneImportExecutionResult> ExecuteAsync(
        LiveSendRunStartRequest request,
        IAsyncEnumerable<ImportedObjectUnit> objectUnits,
        LiveSendRunExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(objectUnits);
        ArgumentNullException.ThrowIfNull(context);

        bool completedSuccessfully = false;
        LiveSendRunState? state = null;

        try
        {
            state = await runStarter.StartAsync(
                request,
                ResoniteLiveSendPhaseContextFactory.CreateRunStartContext(context),
                cancellationToken);

            LiveSendEnqueueContext enqueueContext = ResoniteLiveSendPhaseContextFactory.CreateEnqueueContext(context);
            await foreach (ImportedObjectUnit objectUnit in objectUnits.WithCancellation(cancellationToken))
            {
                int sourceUnitsSeen = Interlocked.Increment(ref state.Progress.SourceObjectUnitCount);
                int sourceCityObjectsSeen = Interlocked.Add(
                    ref state.Progress.SourceCityObjectCount,
                    objectUnit.CityObjects.Count);
                await ResoniteLiveSendQueue.QueueUnitAsync(
                    state,
                    objectUnit,
                    enqueueContext,
                    cancellationToken);
                if (sourceUnitsSeen % 25 == 0)
                {
                    PlateauDiagnostics.Progress(
                        "Live send ingest progress: phase=streaming, source_units_seen={SourceObjectUnitCount}, source_city_objects_seen={SourceCityObjectCount}, queued={QueuedSourceCount}, sent={SentCount}, failed={FailedCount}, backlog={BacklogCount}.",
                        sourceUnitsSeen,
                        sourceCityObjectsSeen,
                        state.Progress.QueuedCityObjectCount,
                        state.Progress.ProcessedCityObjectCount,
                        state.Progress.FailedCityObjectCount,
                        Math.Max(0, state.Progress.QueuedCityObjectCount - state.Progress.ProcessedCityObjectCount - state.Progress.FailedCityObjectCount));
                }
            }

            SceneImportExecutionResult result = await ResoniteLiveSendQueue.CompleteAsync(
                state,
                ResoniteLiveSendPhaseContextFactory.CreateFinalizationContext(context, enqueueContext),
                cancellationToken);
            completedSuccessfully = true;
            return result;
        }
        finally
        {
            await ResoniteLiveSendRunResourceReleaser.ReleaseAsync(
                state,
                context.ClientSession,
                disposeClients: false,
                resetClients: !completedSuccessfully);
        }
    }
}
