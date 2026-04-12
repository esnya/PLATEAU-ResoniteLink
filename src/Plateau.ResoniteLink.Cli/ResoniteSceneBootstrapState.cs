namespace Plateau.ResoniteLink.Cli;

internal readonly record struct ResoniteSceneBootstrapState(
    ResoniteLinkSceneBuilder.CreatedSlot DatasetRootSlot,
    ResoniteLinkSceneBuilder.CreatedSlot DatasetAssetsRootSlot,
    ResoniteLinkSceneBuilder.CreatedSlot CommonAssetsRootSlot,
    bool DatasetRootExisted,
    SceneAnchor SceneAnchor);
