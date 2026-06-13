using System.Collections.Generic;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

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
    string? SourceFileRelativePath = null,
    string? SourceFileRootMeshCode = null,
    bool Landmark = false,
    DistanceCullingClass? DistanceCullingClass = null)
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
        string? SourceFileRelativePath = null,
        string? SourceFileRootMeshCode = null,
        bool Landmark = false,
        DistanceCullingClass? DistanceCullingClass = null)
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
            SourceFileRelativePath,
            SourceFileRootMeshCode,
            Landmark,
            DistanceCullingClass)
    {
    }

    public ResoniteImportedMesh Mesh => Geometry is ResoniteTriangleMeshGeometry triangleMesh
        ? triangleMesh.Mesh
        : throw new System.InvalidOperationException("This city object does not use triangle mesh geometry.");
}
