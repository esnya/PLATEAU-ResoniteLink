using System;

using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class ResoniteLiveSceneImportTargetRuntime
{
    public ResoniteLiveSceneImportTargetRuntime(
        ResoniteLiveSceneImportTargetOptions options,
        ILiveSendClientSession clientSession,
        ResoniteLinkSendDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clientSession);
        ArgumentNullException.ThrowIfNull(diagnostics);

        ClientSession = clientSession;
        Diagnostics = diagnostics;
        ExecutionContext = new ResoniteLiveSceneImportExecutionContext(
            options.MemoryProfile,
            options.ConnectionCount,
            options.EnableMeshBake,
            new ResoniteLiveSendTargetContext(
                options.Endpoint,
                options.ConnectionCount,
                clientSession,
                diagnostics,
                options.ProgressReporter));
    }

    public ILiveSendClientSession ClientSession { get; }

    public ResoniteLinkSendDiagnostics Diagnostics { get; }

    public ResoniteLiveSceneImportExecutionContext ExecutionContext { get; }
}
