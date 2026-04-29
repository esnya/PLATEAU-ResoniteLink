using System.Collections.Generic;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

public sealed record ResoniteConstructionCityObject(
    string SlotKey,
    string DisplayName,
    string PackageName,
    string ActualMeshCode,
    DetailLevel? DetailLevel,
    ResoniteTransform Transform,
    ResoniteConstructionGeometry Geometry,
    IReadOnlyList<ResoniteMaterialBinding> Materials,
    bool CollisionEnabled = true,
    string? SourceFileRelativePath = null)
{
    public ResoniteConstructionCityObject(
        string SlotKey,
        string DisplayName,
        string PackageName,
        string ActualMeshCode,
        DetailLevel? DetailLevel,
        ResoniteTransform Transform,
        ResoniteImportedMesh Mesh,
        IReadOnlyList<ResoniteMaterialBinding> Materials,
        bool CollisionEnabled = true,
        string? SourceFileRelativePath = null)
        : this(
            SlotKey,
            DisplayName,
            PackageName,
            ActualMeshCode,
            DetailLevel,
            Transform,
            new ResoniteTriangleMeshGeometry(Mesh),
            Materials,
            CollisionEnabled,
            SourceFileRelativePath)
    {
    }

    public ResoniteImportedMesh Mesh => Geometry is ResoniteTriangleMeshGeometry triangleMesh
        ? triangleMesh.Mesh
        : throw new System.InvalidOperationException("This city object does not use triangle mesh geometry.");
}
