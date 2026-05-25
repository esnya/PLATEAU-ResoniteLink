using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSceneImportExecutor
{
    Task<SceneImportExecutionResult> ExecuteAsync(
        SceneImportExecutionPlan plan,
        IAsyncEnumerable<ImportedObjectUnit> objectUnits,
        ResoniteLiveSceneImportExecutionContext context,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteLiveSceneImportExecutor(
    IResoniteLiveSendStartRequestFactory startRequestFactory,
    IResoniteLiveSendRunStarter runStarter,
    IResoniteLiveSendContextFactory contextFactory,
    IResoniteLiveSendResourceReleaser resourceReleaser,
    IResoniteLiveSendQueue queue) : IResoniteLiveSceneImportExecutor
{
    public async Task<SceneImportExecutionResult> ExecuteAsync(
        SceneImportExecutionPlan plan,
        IAsyncEnumerable<ImportedObjectUnit> objectUnits,
        ResoniteLiveSceneImportExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(objectUnits);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.LiveSendContext);

        bool completedSuccessfully = false;
        ExceptionDispatchInfo? failure = null;
        LiveSendRunState? state = null;

        try
        {
            state = await runStarter.StartAsync(
                startRequestFactory.Create(
                    plan,
                    context.MemoryProfile,
                    context.ConnectionCount,
                    context.MeshBakeEnabled),
                contextFactory.CreateRunStart(context.LiveSendContext),
                cancellationToken);

            await foreach (ImportedObjectUnit objectUnit in objectUnits.WithCancellation(cancellationToken))
            {
                await queue.QueueUnitAsync(
                    state,
                    objectUnit,
                    contextFactory.CreateEnqueue(context.LiveSendContext),
                    cancellationToken);
            }

            SceneImportExecutionResult result = await queue.CompleteAsync(
                state,
                contextFactory.CreateFinalization(context.LiveSendContext),
                cancellationToken);
            completedSuccessfully = true;
            return result;
        }
        catch (Exception exception)
        {
            failure = ExceptionDispatchInfo.Capture(exception);
            throw;
        }
        finally
        {
            try
            {
                await resourceReleaser.ReleaseAsync(
                    new ResoniteLiveSendResourceRelease(
                        state,
                        context.LiveSendContext.ClientSession,
                        completedSuccessfully
                            ? ResoniteLiveSendClientRelease.None
                            : ResoniteLiveSendClientRelease.Reset));
            }
#pragma warning disable CA1031
            catch when (failure is not null)
            {
            }
#pragma warning restore CA1031
        }
    }
}
