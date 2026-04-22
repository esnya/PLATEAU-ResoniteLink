using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed record ResolvedMaterial(
    MaterialType MaterialType,
    ResoniteTexturePayload? TexturePayload,
    TextureSourceKind TextureSourceKind,
    MaterialProjection Projection,
    string? Family,
    ResoniteFloat2? TextureScale,
    MaterialReuseScope ReuseScope,
    TerrainTextureOverlay? TerrainOverlay = null,
    int? BundledVariantIndex = null);
