using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;

namespace PlateauResoniteLink.Application.Importing;

internal sealed record ResolvedMaterial(
    ResoniteMaterialType MaterialType,
    ResoniteTexturePayload? TexturePayload,
    ResoniteTextureSourceKind TextureSourceKind,
    ResoniteMaterialProjection Projection,
    string? Family,
    ResoniteFloat2? TextureScale,
    ResoniteMaterialAssetScope AssetScope,
    TerrainTextureOverlay? TerrainOverlay = null,
    int? BundledVariantIndex = null);
