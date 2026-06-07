using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

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
        ResoniteLinkSendDiagnostics Diagnostics,
        ILogger Logger)
    {
        ArgumentNullException.ThrowIfNull(Endpoint);
        ArgumentOutOfRangeException.ThrowIfLessThan(ConnectionCount, 1);
        ArgumentNullException.ThrowIfNull(ClientSession);
        ArgumentNullException.ThrowIfNull(Diagnostics);

        this.Endpoint = Endpoint;
        this.ConnectionCount = ConnectionCount;
        this.ClientSession = ClientSession;
        this.Diagnostics = Diagnostics;
        this.Logger = Logger;
    }

    public Uri Endpoint { get; }

    public int ConnectionCount { get; }

    public ILiveSendClientSession ClientSession { get; }

    public ResoniteLinkSendDiagnostics Diagnostics { get; }

    public ILogger Logger { get; }
}

internal sealed class ResoniteLiveSendRunExecutor(
    IResoniteLiveSendRunStarter runStarter,
    ResoniteLiveSendQueue queue)
{
    private readonly IResoniteLiveSendRunStarter runStarter =
        runStarter ?? throw new ArgumentNullException(nameof(runStarter));
    private readonly ResoniteLiveSendQueue queue =
        queue ?? throw new ArgumentNullException(nameof(queue));
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
                await queue.QueueUnitAsync(
                    state,
                    objectUnit,
                    enqueueContext,
                    cancellationToken);
                if (sourceUnitsSeen % 25 == 0)
                {
                    context.Logger.WriteInformation(
                        "Live send ingest progress: phase=streaming, source_units_seen={SourceObjectUnitCount}, source_city_objects_seen={SourceCityObjectCount}, queued={QueuedSourceCount}, sent={SentCount}, failed={FailedCount}, backlog={BacklogCount}.",
                        sourceUnitsSeen,
                        sourceCityObjectsSeen,
                        state.Progress.QueuedCityObjectCount,
                        state.Progress.ProcessedCityObjectCount,
                        state.Progress.FailedCityObjectCount,
                        Math.Max(0, state.Progress.QueuedCityObjectCount - state.Progress.ProcessedCityObjectCount - state.Progress.FailedCityObjectCount));
                }
            }

            SceneImportExecutionResult result = await queue.CompleteAsync(
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
