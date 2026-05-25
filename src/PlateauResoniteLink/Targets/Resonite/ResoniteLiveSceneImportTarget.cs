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
    private readonly IResoniteLiveSendStartRequestFactory startRequestFactory;
    private readonly IResoniteLiveSendRunStarter runStarter;
    private readonly IResoniteLiveSendContextFactory contextFactory;
    private readonly IResoniteLiveSendResourceReleaser resourceReleaser;
    private readonly IResoniteLiveSendQueue queue;
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
        ArgumentNullException.ThrowIfNull(dependencies.StartRequestFactory);
        ArgumentNullException.ThrowIfNull(dependencies.RunStarter);
        ArgumentNullException.ThrowIfNull(dependencies.ContextFactory);
        ArgumentNullException.ThrowIfNull(dependencies.ResourceReleaser);
        ArgumentNullException.ThrowIfNull(dependencies.Queue);

        endpoint = options.Endpoint;
        connectionCount = options.ConnectionCount;
        MemoryProfile = options.MemoryProfile;
        Diagnostics = dependencies.Diagnostics;
        MeshBakeEnabled = options.EnableMeshBake;
        progressReporter = options.ProgressReporter;
        startRequestFactory = dependencies.StartRequestFactory;
        runStarter = dependencies.RunStarter;
        contextFactory = dependencies.ContextFactory;
        resourceReleaser = dependencies.ResourceReleaser;
        queue = dependencies.Queue;
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
        bool completedSuccessfully = false;
        LiveSendRunState? state = null;

        try
        {
            ResoniteLiveSendTargetContext liveSendContext = CreateLiveSendContext();
            state = await runStarter.StartAsync(
                startRequestFactory.Create(
                    plan,
                    MemoryProfile,
                    connectionCount,
                    MeshBakeEnabled),
                contextFactory.CreateRunStart(liveSendContext),
                cancellationToken);

            await foreach (ImportedObjectUnit objectUnit in objectUnits.WithCancellation(cancellationToken))
            {
                await queue.QueueUnitAsync(
                    state,
                    objectUnit,
                    contextFactory.CreateEnqueue(liveSendContext),
                    cancellationToken);
            }

            SceneImportExecutionResult result = await queue.CompleteAsync(
                state,
                contextFactory.CreateFinalization(liveSendContext),
                cancellationToken);
            completedSuccessfully = true;
            return result;
        }
        finally
        {
            try
            {
                await resourceReleaser.ReleaseAsync(
                    CreateResourceRelease(
                        state,
                        completedSuccessfully
                            ? ResoniteLiveSendClientRelease.None
                            : ResoniteLiveSendClientRelease.Reset));
            }
            finally
            {
                Volatile.Write(ref executionClaimed, 0);
            }
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
