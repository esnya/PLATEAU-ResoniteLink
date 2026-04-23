using System.Collections.Generic;

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
    string? SourceObjectKey = null,
    string? SourceUnitKey = null,
    string? SourceFileRelativePath = null,
    bool UsesFallbackRoofStrategy = false)
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
        string? SourceFileRelativePath = null,
        bool UsesFallbackRoofStrategy = false)
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
            SourceFileRelativePath,
            UsesFallbackRoofStrategy)
    {
    }

    public ResoniteImportedMesh Mesh => Geometry is ResoniteTriangleMeshGeometry triangleMesh
        ? triangleMesh.Mesh
        : throw new System.InvalidOperationException("This city object does not use triangle mesh geometry.");
}
