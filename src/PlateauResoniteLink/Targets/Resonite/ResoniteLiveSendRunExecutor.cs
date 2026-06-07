using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record LiveSendRunExecutionContext
{
    public LiveSendRunExecutionContext(
        Uri Endpoint,
        int ConnectionCount,
        ILiveSendClientSession ClientSession,
        ResoniteLinkSendDiagnostics Diagnostics,
        Action<string>? ProgressReporter)
    {
        ArgumentNullException.ThrowIfNull(Endpoint);
        ArgumentOutOfRangeException.ThrowIfLessThan(ConnectionCount, 1);
        ArgumentNullException.ThrowIfNull(ClientSession);
        ArgumentNullException.ThrowIfNull(Diagnostics);

        this.Endpoint = Endpoint;
        this.ConnectionCount = ConnectionCount;
        this.ClientSession = ClientSession;
        this.Diagnostics = Diagnostics;
        this.ProgressReporter = ProgressReporter;
    }

    public Uri Endpoint { get; }

    public int ConnectionCount { get; }

    public ILiveSendClientSession ClientSession { get; }

    public ResoniteLinkSendDiagnostics Diagnostics { get; }

    public Action<string>? ProgressReporter { get; }
}

internal sealed class ResoniteLiveSendRunExecutor(
    IResoniteLiveSendRunStarter runStarter,
    IResoniteLiveSendQueue queue)
{
    private readonly IResoniteLiveSendRunStarter runStarter =
        runStarter ?? throw new ArgumentNullException(nameof(runStarter));
    private readonly IResoniteLiveSendQueue queue =
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
                await queue.QueueUnitAsync(
                    state,
                    objectUnit,
                    enqueueContext,
                    cancellationToken);
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
