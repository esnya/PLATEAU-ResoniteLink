using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record ResoniteLiveSceneImportDependencies(
    ILiveSendClientSession ClientSession,
    ResoniteLinkSendDiagnostics Diagnostics,
    IResoniteLiveSceneImportExecutor Executor,
    IResoniteLiveSendResourceReleaser ResourceReleaser)
{
    public ResoniteLiveSceneImportTargetRuntime CreateRuntime(
        ResoniteLiveSceneImportTargetOptions options)
    {
        return new ResoniteLiveSceneImportTargetRuntime(
            options,
            ClientSession,
            Diagnostics);
    }
}
