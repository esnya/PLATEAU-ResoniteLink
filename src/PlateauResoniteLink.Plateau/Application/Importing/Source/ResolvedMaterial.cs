using PlateauResoniteLink.Core.Domain.Importing;
using PlateauResoniteLink.Core.Application.Importing.Contracts;

namespace PlateauResoniteLink.Plateau.Application.Importing.Source;

internal sealed record ResolvedMaterial(
    MaterialType MaterialType,
    TexturePayload? TexturePayload,
    TextureSourceKind TextureSourceKind,
    MaterialProjection Projection,
    string? Family,
    Float2? TextureScale,
    MaterialReuseScope ReuseScope,
    TerrainTextureOverlay? TerrainOverlay = null,
    int? BundledVariantIndex = null,
    Float2? TextureOffset = null,
    DefaultCommonMaterialMember? CommonMaterial = null);
