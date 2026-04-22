using System.Collections.Generic;

namespace PlateauResoniteLink.Targets.Resonite;

public sealed record ResoniteMeshSubmesh(
    int Index,
    string MaterialKey,
    IReadOnlyList<int> TriangleVertexIndices);
