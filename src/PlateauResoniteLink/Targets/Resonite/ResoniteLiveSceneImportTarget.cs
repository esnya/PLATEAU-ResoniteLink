using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

public sealed class ResoniteLiveSceneImportTarget : ISceneSink
{
    private readonly Uri endpoint;
    private readonly int connectionCount;
    private readonly IResoniteLiveSceneImportExecutor executor;
    private readonly IResoniteLiveSendResourceReleaser resourceReleaser;
#pragma warning disable CA1859
    private ILiveSendClientSession ClientSessionInternal { get; }
#pragma warning restore CA1859
    private readonly Action<string>? progressReporter;

    private int executionClaimed;

    internal ResoniteLiveSceneImportTarget(
        ResoniteLiveSceneImportTargetOptions options,
        ResoniteLiveSceneImportDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(dependencies.ClientSession);
        ArgumentNullException.ThrowIfNull(dependencies.Executor);
        ArgumentNullException.ThrowIfNull(dependencies.ResourceReleaser);

        endpoint = options.Endpoint;
        connectionCount = options.ConnectionCount;
        MemoryProfile = options.MemoryProfile;
        Diagnostics = dependencies.Diagnostics;
        MeshBakeEnabled = options.EnableMeshBake;
        progressReporter = options.ProgressReporter;
        executor = dependencies.Executor;
        resourceReleaser = dependencies.ResourceReleaser;
        ClientSessionInternal = dependencies.ClientSession;
    }

    internal bool MeshBakeEnabled { get; }

    internal ResoniteLinkSendDiagnostics Diagnostics { get; }

    internal ILiveSendClientSession ClientSession => ClientSessionInternal;

    internal ResoniteImportMemoryProfile MemoryProfile { get; }

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
                CreateExecutionContext(),
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
            ClientSessionInternal,
            clientRelease);
    }

    private ResoniteLiveSceneImportExecutionContext CreateExecutionContext()
    {
        return new ResoniteLiveSceneImportExecutionContext(
            MemoryProfile,
            connectionCount,
            MeshBakeEnabled,
            CreateLiveSendContext());
    }

    private ResoniteLiveSendTargetContext CreateLiveSendContext()
    {
        return new ResoniteLiveSendTargetContext(
            endpoint,
            connectionCount,
            ClientSessionInternal,
            Diagnostics,
            progressReporter);
    }
}
