namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record ResoniteMaterialBinding(
    string MaterialKey,
    ResoniteColor BaseColor,
    string? TexturePath,
    ResoniteTextureSourceKind TextureSourceKind,
    ResoniteMaterialProjection Projection,
    IReadOnlyList<int> SubmeshIndices);
