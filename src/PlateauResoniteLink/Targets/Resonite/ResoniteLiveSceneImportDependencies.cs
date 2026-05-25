using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record ResoniteLiveSceneImportDependencies(
    ILiveSendClientSession ClientSession,
    IResoniteLiveSendStartRequestFactory StartRequestFactory,
    IResoniteLiveSendRunExecutor RunExecutor,
    IResoniteLiveSendRunResourceReleaser ResourceReleaser);
