using System.Collections.Generic;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

public sealed record ResoniteMeshSubmesh(
    int Index,
    IReadOnlyList<int> TriangleVertexIndices);
