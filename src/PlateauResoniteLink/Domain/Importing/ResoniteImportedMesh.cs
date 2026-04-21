using System.Collections.Generic;

namespace PlateauResoniteLink.Domain.Importing;

public sealed record ResoniteImportedMesh(
    IReadOnlyList<ResoniteMeshVertex> Vertices,
    IReadOnlyList<ResoniteMeshSubmesh> Submeshes);
