using System.Collections.Generic;

using PlateauResoniteLink.Targets.Resonite.Execution;

using ResoniteLink;
using PlateauResoniteLink.Application.Importing.Contracts;

namespace PlateauResoniteLink.Targets.Resonite;

internal readonly record struct ResoniteSceneSetupState(
    CreatedSlot DatasetRootSlot,
    CreatedSlot DatasetAssetsRootSlot,
    CreatedSlot CommonAssetsRootSlot,
    bool DatasetRootExisted,
    SceneAnchor SceneAnchor,
    Slot? DatasetRootSnapshot,
    CommonMaterialCatalog<ResoniteCommonMaterialAsset> CommonMaterialAssets,
    IReadOnlyCollection<string> CommonMaterialFamilies);
