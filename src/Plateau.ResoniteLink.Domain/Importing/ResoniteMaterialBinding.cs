namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record ResoniteMaterialBinding(
    string MaterialKey,
    ResoniteColor BaseColor,
    ResoniteMaterialType MaterialType,
    string? TexturePath,
    ResoniteTextureSourceKind TextureSourceKind,
    ResoniteMaterialProjection Projection,
    ResoniteMaterialDepthOffset? DepthOffset,
    IReadOnlyList<int> SubmeshIndices,
    ResoniteFloat2? TextureScale = null);
