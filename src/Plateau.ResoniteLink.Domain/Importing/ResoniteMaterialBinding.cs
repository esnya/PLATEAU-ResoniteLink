namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record ResoniteMaterialBinding(
    string MaterialKey,
    ResoniteColor BaseColor,
    string? TexturePath,
    IReadOnlyList<int> SubmeshIndices);
