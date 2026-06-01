using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

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
    ThirdRegionalMeshCode? TerrainMeshCode);
