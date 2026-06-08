using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

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
                await ResoniteLiveSendQueue.QueueUnitAsync(
                    state,
                    objectUnit,
                    enqueueContext,
                    cancellationToken);
            }

            SceneImportExecutionResult result = await ResoniteLiveSendQueue.CompleteAsync(
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
            await ResoniteLiveSendRunResourceReleaser.ReleaseAsync(
                state,
                context.ClientSession,
                disposeClients: false,
                resetClients: !completedSuccessfully);
        }
    }
}
