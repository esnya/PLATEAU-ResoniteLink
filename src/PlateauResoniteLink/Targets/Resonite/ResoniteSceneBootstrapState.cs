using System.Collections.Generic;

using PlateauResoniteLink.Targets.Resonite.Execution;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal readonly record struct ResoniteSceneBootstrapState(
    CreatedSlot DatasetRootSlot,
    CreatedSlot DatasetAssetsRootSlot,
    CreatedSlot CommonAssetsRootSlot,
    bool DatasetRootExisted,
    SceneAnchor SceneAnchor,
    Slot? DatasetRootSnapshot,
    IReadOnlyDictionary<string, CreatedMaterialAsset> CommonMaterialAssetsByKey,
    IReadOnlyCollection<string> CommonMaterialFamilies);
