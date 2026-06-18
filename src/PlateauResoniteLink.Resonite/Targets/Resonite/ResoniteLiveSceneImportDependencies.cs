using PlateauResoniteLink.Resonite.Transport.ResoniteLink;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal sealed record ResoniteLiveSceneImportDependencies(
    ILiveSendClientSession ClientSession,
    ResoniteLinkSendDiagnostics Diagnostics,
    IResoniteLiveSendRunExecutor RunExecutor);
