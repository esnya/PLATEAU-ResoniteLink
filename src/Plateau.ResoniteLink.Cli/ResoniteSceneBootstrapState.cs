namespace Plateau.ResoniteLink.Cli;

internal readonly record struct ResoniteSceneBootstrapState(
    CreatedSlot DatasetRootSlot,
    CreatedSlot DatasetAssetsRootSlot,
    CreatedSlot CommonAssetsRootSlot,
    bool DatasetRootExisted,
    SceneAnchor SceneAnchor);
