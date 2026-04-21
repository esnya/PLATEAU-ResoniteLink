using System.Collections.Generic;

namespace PlateauResoniteLink.Domain.Importing;

public abstract record ResoniteConstructionGeometry;

public sealed record ResoniteTriangleMeshGeometry(
    ResoniteImportedMesh Mesh)
    : ResoniteConstructionGeometry;

public sealed record ResoniteHeightMapGridGeometry(
    int Width,
    int Height,
    ResoniteFloat2 Size,
    double MinHeight,
    double MaxHeight,
    IReadOnlyList<double> HeightSamples)
    : ResoniteConstructionGeometry;
