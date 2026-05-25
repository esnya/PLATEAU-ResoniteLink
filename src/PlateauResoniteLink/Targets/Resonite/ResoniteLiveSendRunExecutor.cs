using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record LiveSendRunExecutionContext(
    Uri Endpoint,
    int ConnectionCount,
    ILiveSendClientSession ClientSession,
    ResoniteLinkSendDiagnostics Diagnostics,
    Action<string>? ProgressReporter);

internal interface IResoniteLiveSendRunExecutor
{
    Task<SceneImportExecutionResult> ExecuteAsync(
        LiveSendRunStartRequest request,
        IAsyncEnumerable<ImportedObjectUnit> objectUnits,
        LiveSendRunExecutionContext context,
        CancellationToken cancellationToken);
}

internal interface IResoniteLiveSendRunExecutorFactory
{
    IResoniteLiveSendRunExecutor Create(IResoniteLiveSendRunStarter runStarter);
}

internal sealed class ResoniteLiveSendRunExecutorFactory(
    IResoniteLiveSendQueue queue,
    IResoniteLiveSendRunResourceReleaser resourceReleaser) : IResoniteLiveSendRunExecutorFactory
{
    public IResoniteLiveSendRunExecutor Create(IResoniteLiveSendRunStarter runStarter)
    {
        ArgumentNullException.ThrowIfNull(runStarter);

        return new ResoniteLiveSendRunExecutor(
            runStarter,
            queue,
            resourceReleaser);
    }
}

internal sealed class ResoniteLiveSendRunExecutor(
    IResoniteLiveSendRunStarter runStarter,
    IResoniteLiveSendQueue queue,
    IResoniteLiveSendRunResourceReleaser resourceReleaser) : IResoniteLiveSendRunExecutor
{
    private readonly IResoniteLiveSendRunStarter runStarter =
        runStarter ?? throw new ArgumentNullException(nameof(runStarter));
    private readonly IResoniteLiveSendQueue queue =
        queue ?? throw new ArgumentNullException(nameof(queue));
    private readonly IResoniteLiveSendRunResourceReleaser resourceReleaser =
        resourceReleaser ?? throw new ArgumentNullException(nameof(resourceReleaser));

    public async Task<SceneImportExecutionResult> ExecuteAsync(
        LiveSendRunStartRequest request,
        IAsyncEnumerable<ImportedObjectUnit> objectUnits,
        LiveSendRunExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(objectUnits);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Endpoint);
        ArgumentOutOfRangeException.ThrowIfLessThan(context.ConnectionCount, 1);
        ArgumentNullException.ThrowIfNull(context.ClientSession);
        ArgumentNullException.ThrowIfNull(context.Diagnostics);

        bool completedSuccessfully = false;
        LiveSendRunState? state = null;

        try
        {
            state = await runStarter.StartAsync(
                request,
                CreateRunStartContext(context),
                cancellationToken);

            LiveSendEnqueueContext enqueueContext = CreateEnqueueContext(context);
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
                CreateFinalizationContext(context, enqueueContext),
                cancellationToken);
            completedSuccessfully = true;
            return result;
        }
        finally
        {
            await resourceReleaser.ReleaseAsync(
                state,
                context.ClientSession,
                disposeClients: false,
                resetClients: !completedSuccessfully);
        }
    }

    private static LiveSendRunStartContext CreateRunStartContext(LiveSendRunExecutionContext context)
    {
        return new LiveSendRunStartContext(
            context.Endpoint,
            context.ClientSession,
            context.Diagnostics,
            context.ProgressReporter);
    }

    private static LiveSendEnqueueContext CreateEnqueueContext(LiveSendRunExecutionContext context)
    {
        return new LiveSendEnqueueContext(
            context.ConnectionCount,
            context.ClientSession.GetRequiredClient,
            context.ProgressReporter);
    }

    private static LiveSendFinalizationContext CreateFinalizationContext(
        LiveSendRunExecutionContext context,
        LiveSendEnqueueContext enqueueContext)
    {
        return new LiveSendFinalizationContext(
            context.Endpoint,
            enqueueContext,
            context.Diagnostics,
            context.ProgressReporter);
    }
}
