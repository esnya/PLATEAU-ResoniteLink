using System;
using System.Collections.Generic;

namespace PlateauResoniteLink.Application.Importing;

public sealed record ImportedCityObject(
    string ObjectKey,
    string DisplayName,
    string PackageName,
    string ActualMeshCode,
    int? LodLevel,
    Transform3D Transform,
    ConstructionGeometry Geometry,
    IReadOnlyList<MaterialBinding> Materials,
    bool CollisionEnabled = true,
    string? SourceObjectKey = null,
    string? SourceUnitKey = null,
    string? SourceFileRelativePath = null)
{
    public ImportedCityObject(
        string ObjectKey,
        string DisplayName,
        string PackageName,
        string ActualMeshCode,
        int? LodLevel,
        Transform3D Transform,
        ImportedMesh Mesh,
        IReadOnlyList<MaterialBinding> Materials,
        bool CollisionEnabled = true,
        string? SourceObjectKey = null,
        string? SourceUnitKey = null,
        string? SourceFileRelativePath = null)
        : this(
            ObjectKey,
            DisplayName,
            PackageName,
            ActualMeshCode,
            LodLevel,
            Transform,
            new TriangleMeshGeometry(Mesh),
            Materials,
            CollisionEnabled,
            SourceObjectKey,
            SourceUnitKey,
            SourceFileRelativePath)
    {
    }

    public ImportedMesh Mesh => Geometry is TriangleMeshGeometry triangleMesh
        ? triangleMesh.Mesh
        : throw new InvalidOperationException("This imported city object does not use triangle mesh geometry.");
}
