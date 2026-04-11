using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public sealed record ResolvedMaterial(
    ResoniteMaterialType MaterialType,
    string? TexturePath,
    ResoniteTextureSourceKind TextureSourceKind,
    ResoniteMaterialProjection Projection,
    string? Family,
    ResoniteFloat2? TextureScale,
    ResoniteMaterialAssetScope AssetScope);
