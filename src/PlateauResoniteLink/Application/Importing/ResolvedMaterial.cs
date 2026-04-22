using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed record ResolvedMaterial(
    MaterialType MaterialType,
    TexturePayload? TexturePayload,
    TextureSourceKind TextureSourceKind,
    MaterialProjection Projection,
    string? Family,
    Float2? TextureScale,
    MaterialReuseScope ReuseScope,
    TerrainTextureOverlay? TerrainOverlay = null,
    int? BundledVariantIndex = null);
