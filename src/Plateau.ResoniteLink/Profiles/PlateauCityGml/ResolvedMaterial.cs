using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public sealed record ResolvedMaterial(
    ResoniteMaterialType MaterialType,
    ResoniteTexturePayload? TexturePayload,
    ResoniteTextureSourceKind TextureSourceKind,
    ResoniteMaterialProjection Projection,
    string? Family,
    ResoniteFloat2? TextureScale,
    ResoniteMaterialAssetScope AssetScope,
    TerrainTextureOverlay? TerrainOverlay = null,
    int? BundledVariantIndex = null);
