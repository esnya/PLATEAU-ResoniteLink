using System.Collections.Generic;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

public sealed record ResoniteMaterialBinding(
    string MaterialKey,
    ResoniteColor BaseColor,
    ResoniteMaterialType MaterialType,
    ResoniteTexturePayload? TexturePayload,
    ResoniteTextureSourceKind TextureSourceKind,
    ResoniteMaterialProjection Projection,
    ResoniteMaterialDepthOffset? DepthOffset,
    IReadOnlyList<int> SubmeshIndices,
    ResoniteFloat2? TextureScale = null,
    string? Family = null,
    ResoniteFloat2? TextureOffset = null,
    ResoniteMaterialAssetScope AssetScope = ResoniteMaterialAssetScope.PresentationSlotScoped,
    TerrainTextureOverlay? TerrainOverlay = null,
    int? BundledVariantIndex = null,
    string? TerrainMeshCode = null);
