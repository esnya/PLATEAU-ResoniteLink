namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record LiveSendSetupInitialization(
    ResoniteSceneSetupState SetupState,
    LiveSendProgressSink Progress,
    CommonMaterialAssetCache Materials,
    ResoniteSharedSlotIndex Placement);
