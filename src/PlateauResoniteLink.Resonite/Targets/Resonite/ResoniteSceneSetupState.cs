using System.Collections.Generic;

using PlateauResoniteLink.Resonite.Targets.Resonite.Execution;

using ResoniteLink;
using PlateauResoniteLink.Core.Application.Importing.Contracts;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal readonly record struct ResoniteSceneSetupState(
    CreatedSlot DatasetRootSlot,
    CreatedSlot DatasetAssetsRootSlot,
    CreatedSlot CommonAssetsRootSlot,
    bool DatasetRootExisted,
    SceneAnchor SceneAnchor,
    Slot? DatasetRootSnapshot,
    CommonMaterialCatalog<ResoniteCommonMaterialAsset> CommonMaterialAssets,
    IReadOnlyCollection<string> CommonMaterialFamilies);
