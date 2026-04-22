using System.Collections.Generic;

namespace PlateauResoniteLink.Targets.Resonite;

public sealed record ResoniteImportedMesh(
    IReadOnlyList<ResoniteMeshVertex> Vertices,
    IReadOnlyList<ResoniteMeshSubmesh> Submeshes);
