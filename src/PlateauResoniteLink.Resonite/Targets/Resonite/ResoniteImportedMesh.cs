using System.Collections.Generic;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

public sealed record ResoniteImportedMesh(
    IReadOnlyList<ResoniteMeshVertex> Vertices,
    IReadOnlyList<ResoniteMeshSubmesh> Submeshes);
