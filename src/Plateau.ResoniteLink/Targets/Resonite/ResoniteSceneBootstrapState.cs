using ResoniteLink;

namespace Plateau.ResoniteLink.Targets.Resonite;

internal readonly record struct ResoniteSceneBootstrapState(
    CreatedSlot DatasetRootSlot,
    CreatedSlot DatasetAssetsRootSlot,
    CreatedSlot CommonAssetsRootSlot,
    bool DatasetRootExisted,
    SceneAnchor SceneAnchor,
    Slot? DatasetRootSnapshot,
    string? ExistingLicenseComponentId,
    string? DatasetLicenseComponentId,
    IReadOnlyDictionary<string, CreatedMaterialAsset> CommonMaterialAssetsByKey,
    IReadOnlyCollection<string> CommonMaterialFamilies);
