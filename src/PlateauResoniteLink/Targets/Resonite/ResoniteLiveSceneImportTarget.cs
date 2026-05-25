using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

public sealed class ResoniteLiveSceneImportTarget : ISceneSink
{
    private readonly ResoniteLiveSceneImportTargetRuntime runtime;
    private readonly IResoniteLiveSceneImportExecutor executor;
    private readonly IResoniteLiveSendResourceReleaser resourceReleaser;

    private int executionClaimed;

    internal ResoniteLiveSceneImportTarget(
        ResoniteLiveSceneImportTargetOptions options,
        ResoniteLiveSceneImportDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(dependencies.ClientSession);
        ArgumentNullException.ThrowIfNull(dependencies.Diagnostics);
        ArgumentNullException.ThrowIfNull(dependencies.Executor);
        ArgumentNullException.ThrowIfNull(dependencies.ResourceReleaser);

        runtime = dependencies.CreateRuntime(options);
        executor = dependencies.Executor;
        resourceReleaser = dependencies.ResourceReleaser;
    }

    internal bool MeshBakeEnabled => runtime.ExecutionContext.MeshBakeEnabled;

    internal ResoniteLinkSendDiagnostics Diagnostics => runtime.Diagnostics;

    internal ILiveSendClientSession ClientSession => runtime.ClientSession;

    internal ResoniteImportMemoryProfile MemoryProfile => runtime.ExecutionContext.MemoryProfile;

    public async Task<SceneImportExecutionResult> ExecuteAsync(
        SceneImportExecutionPlan plan,
        IAsyncEnumerable<ImportedObjectUnit> objectUnits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(objectUnits);
        if (Interlocked.Exchange(ref executionClaimed, 1) != 0)
        {
            throw new InvalidOperationException("A live scene import run is already active on this live scene import target instance.");
        }
        try
        {
            return await executor.ExecuteAsync(
                plan,
                objectUnits,
                runtime.ExecutionContext,
                cancellationToken);
        }
        finally
        {
            Volatile.Write(ref executionClaimed, 0);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await resourceReleaser.ReleaseAsync(
            CreateResourceRelease(
                state: null,
                clientRelease: ResoniteLiveSendClientRelease.Dispose));
    }

    private ResoniteLiveSendResourceRelease CreateResourceRelease(
        LiveSendRunState? state,
        ResoniteLiveSendClientRelease clientRelease)
    {
        return new ResoniteLiveSendResourceRelease(
            state,
            runtime.ClientSession,
            clientRelease);
    }
}
