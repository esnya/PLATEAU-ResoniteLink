using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

public sealed class ResoniteLiveSceneImportTarget : ISceneSink
{
    private readonly Uri endpoint;
    private readonly int connectionCount;
    private readonly IResoniteLiveSendStartRequestFactory startRequestFactory;
    private readonly ResoniteLiveSendRunExecutor runExecutor;
#pragma warning disable CA1859
    private ILiveSendClientSession ClientSessionInternal { get; }
#pragma warning restore CA1859
    private readonly ILogger logger;

    private int executionClaimed;

    internal ResoniteLiveSceneImportTarget(
        ResoniteLiveSceneImportTargetOptions options,
        ResoniteLiveSceneImportDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(dependencies.ClientSession);
        ArgumentNullException.ThrowIfNull(dependencies.StartRequestFactory);
        ArgumentNullException.ThrowIfNull(dependencies.RunExecutor);

        endpoint = options.Endpoint;
        connectionCount = options.ConnectionCount;
        MemoryProfile = options.MemoryProfile;
        Diagnostics = dependencies.Diagnostics;
        MeshBakeEnabled = options.EnableMeshBake;
        logger = options.LoggerFactory.CreateLogger("PlateauResoniteLink.LiveSend");
        startRequestFactory = dependencies.StartRequestFactory;
        runExecutor = dependencies.RunExecutor;
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
            return await runExecutor.ExecuteAsync(
                startRequestFactory.Create(
                    plan,
                    MemoryProfile,
                    connectionCount,
                    MeshBakeEnabled),
                objectUnits,
                new LiveSendRunExecutionContext(
                    endpoint,
                    connectionCount,
                    ClientSessionInternal,
                    Diagnostics,
                    logger),
                cancellationToken);
        }
        finally
        {
            Volatile.Write(ref executionClaimed, 0);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ResoniteLiveSendRunResourceReleaser.ReleaseAsync(
            state: null,
            clientSession: ClientSessionInternal,
            disposeClients: true,
            resetClients: false);
    }

}
