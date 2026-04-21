using System.Collections.Generic;

namespace PlateauResoniteLink.Domain.Importing;

public sealed record ResoniteConstructionCityObject(
    string SlotKey,
    string DisplayName,
    string PackageName,
    string ActualMeshCode,
    int? LodLevel,
    ResoniteTransform Transform,
    ResoniteConstructionGeometry Geometry,
    IReadOnlyList<ResoniteMaterialBinding> Materials,
    bool CollisionEnabled = true,
    string? SourceObjectKey = null,
    string? SourceUnitKey = null,
    string? SourceFileRelativePath = null)
{
    public ResoniteConstructionCityObject(
        string SlotKey,
        string DisplayName,
        string PackageName,
        string ActualMeshCode,
        int? LodLevel,
        ResoniteTransform Transform,
        ResoniteImportedMesh Mesh,
        IReadOnlyList<ResoniteMaterialBinding> Materials,
        bool CollisionEnabled = true,
        string? SourceObjectKey = null,
        string? SourceUnitKey = null,
        string? SourceFileRelativePath = null)
        : this(
            SlotKey,
            DisplayName,
            PackageName,
            ActualMeshCode,
            LodLevel,
            Transform,
            new ResoniteTriangleMeshGeometry(Mesh),
            Materials,
            CollisionEnabled,
            SourceObjectKey,
            SourceUnitKey,
            SourceFileRelativePath)
    {
    }

    public ResoniteImportedMesh Mesh => Geometry is ResoniteTriangleMeshGeometry triangleMesh
        ? triangleMesh.Mesh
        : throw new System.InvalidOperationException("This city object does not use triangle mesh geometry.");
}
