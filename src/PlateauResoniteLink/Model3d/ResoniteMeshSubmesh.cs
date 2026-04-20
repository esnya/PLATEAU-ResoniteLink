namespace PlateauResoniteLink.Domain.Importing;

public sealed record ResoniteMeshSubmesh(
    int Index,
    string MaterialKey,
    IReadOnlyList<int> TriangleVertexIndices);
