using System;
using System.Collections.Generic;

namespace PlateauResoniteLink.Application.Importing;

public sealed record ImportedCityObject(
    string ObjectKey,
    string DisplayName,
    string PackageName,
    string ActualMeshCode,
    DetailLevel? DetailLevel,
    Transform3D Transform,
    ConstructionGeometry Geometry,
    IReadOnlyList<MaterialBinding> Materials,
    bool CollisionEnabled = true,
    string? SourceFileRelativePath = null)
{
    public ImportedCityObject(
        string ObjectKey,
        string DisplayName,
        string PackageName,
        string ActualMeshCode,
        DetailLevel? DetailLevel,
        Transform3D Transform,
        ImportedMesh Mesh,
        IReadOnlyList<MaterialBinding> Materials,
        bool CollisionEnabled = true,
        string? SourceFileRelativePath = null)
        : this(
            ObjectKey,
            DisplayName,
            PackageName,
            ActualMeshCode,
            DetailLevel,
            Transform,
            new TriangleMeshGeometry(Mesh),
            Materials,
            CollisionEnabled,
            SourceFileRelativePath)
    {
    }

    public ImportedMesh Mesh => Geometry is TriangleMeshGeometry triangleMesh
        ? triangleMesh.Mesh
        : throw new InvalidOperationException("This imported city object does not use triangle mesh geometry.");
}
