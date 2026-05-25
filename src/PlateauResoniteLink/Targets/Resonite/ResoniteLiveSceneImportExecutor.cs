using System.Collections.Generic;
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
        bool completedSuccessfully = false;
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
        finally
        {
            await resourceReleaser.ReleaseAsync(
                new ResoniteLiveSendResourceRelease(
                    state,
                    context.LiveSendContext.ClientSession,
                    completedSuccessfully
                        ? ResoniteLiveSendClientRelease.None
                        : ResoniteLiveSendClientRelease.Reset));
        }
    }
}

internal sealed record ResoniteLiveSceneImportExecutionContext(
    ResoniteImportMemoryProfile MemoryProfile,
    int ConnectionCount,
    bool MeshBakeEnabled,
    ResoniteLiveSendTargetContext LiveSendContext);
