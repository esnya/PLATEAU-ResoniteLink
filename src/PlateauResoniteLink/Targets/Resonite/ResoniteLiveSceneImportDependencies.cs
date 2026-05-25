using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record ResoniteLiveSceneImportDependencies(
    ILiveSendClientSession ClientSession,
    ResoniteLinkSendDiagnostics Diagnostics,
    IResoniteLiveSendStartRequestFactory StartRequestFactory,
    IResoniteLiveSendRunStarter RunStarter,
    IResoniteLiveSendContextFactory ContextFactory,
    IResoniteLiveSendResourceReleaser ResourceReleaser,
    IResoniteLiveSendQueue Queue);
