using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Application.Importing.Contracts;

namespace PlateauResoniteLink.Targets.Resonite;

internal readonly record struct NonDemPreservedMaterialGroupingKey(
    DefaultCommonMaterialMember? CommonMaterial,
    ResoniteColor BaseColor,
    ResoniteMaterialType MaterialType,
    ResoniteTexturePayload? TexturePayload,
    ResoniteTextureSourceKind TextureSourceKind,
    TerrainTextureOverlay? TerrainOverlay,
    ResoniteMaterialProjection Projection,
    ResoniteMaterialDepthOffset? DepthOffset,
    ResoniteFloat2? TextureScale,
    ResoniteFloat2? TextureOffset,
    ResoniteMaterialAssetScope AssetScope,
    string? Family,
    int? BundledVariantIndex,
    string? TerrainMeshCode);
