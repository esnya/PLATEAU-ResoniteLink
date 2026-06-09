using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSendRunExecutor
{
    Task<SceneImportExecutionResult> ExecuteAsync(
        LiveSendRunStartRequest request,
        IAsyncEnumerable<ImportedObjectUnit> objectUnits,
        LiveSendRunExecutionContext context,
        CancellationToken cancellationToken);
}

internal delegate IResoniteLiveSendRunExecutor CreateResoniteLiveSendRunExecutor(
    ResoniteLiveSendRunStarter runStarter);

internal delegate Task QueueLiveSendUnit(
    LiveSendRunState state,
    ImportedObjectUnit objectUnit,
    LiveSendEnqueueContext context,
    CancellationToken cancellationToken);

internal delegate Task<SceneImportExecutionResult> CompleteLiveSendQueue(
    LiveSendRunState state,
    LiveSendFinalizationContext context,
    CancellationToken cancellationToken);

internal delegate ValueTask ReleaseLiveSendRunResources(
    LiveSendRunState? state,
    ILiveSendClientSession clientSession,
    bool disposeClients,
    bool resetClients);

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
    ResoniteLiveSendRunStarter runStarter,
    QueueLiveSendUnit queueUnit,
    CompleteLiveSendQueue completeQueue,
    ReleaseLiveSendRunResources releaseResources)
    : IResoniteLiveSendRunExecutor
{
    private readonly ResoniteLiveSendRunStarter runStarter =
        runStarter ?? throw new ArgumentNullException(nameof(runStarter));
    private readonly QueueLiveSendUnit queueUnit =
        queueUnit ?? throw new ArgumentNullException(nameof(queueUnit));
    private readonly CompleteLiveSendQueue completeQueue =
        completeQueue ?? throw new ArgumentNullException(nameof(completeQueue));
    private readonly ReleaseLiveSendRunResources releaseResources =
        releaseResources ?? throw new ArgumentNullException(nameof(releaseResources));

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
                new LiveSendRunStartContext(
                    context.Endpoint,
                    context.ClientSession,
                    context.Diagnostics,
                    context.ProgressReporter),
                cancellationToken);

            LiveSendEnqueueContext enqueueContext = new(
                context.ConnectionCount,
                context.ClientSession.GetRequiredClient,
                context.ProgressReporter);
            await foreach (ImportedObjectUnit objectUnit in objectUnits.WithCancellation(cancellationToken))
            {
                await queueUnit(
                    state,
                    objectUnit,
                    enqueueContext,
                    cancellationToken);
            }

            SceneImportExecutionResult result = await completeQueue(
                state,
                new LiveSendFinalizationContext(
                    context.Endpoint,
                    enqueueContext,
                    context.Diagnostics,
                    context.ProgressReporter),
                cancellationToken);
            completedSuccessfully = true;
            return result;
        }
        finally
        {
            await releaseResources(
                state,
                context.ClientSession,
                disposeClients: false,
                resetClients: !completedSuccessfully);
        }
    }
}
