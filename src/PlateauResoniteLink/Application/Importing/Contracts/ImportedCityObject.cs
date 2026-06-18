using System;
using System.Collections.Generic;

namespace PlateauResoniteLink.Application.Importing.Contracts;

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
    string? SourceFileRelativePath = null,
    string? SourceFileRootMeshCode = null,
    bool Landmark = false,
    DistanceCullingClass? DistanceCullingClass = null)
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
        string? SourceFileRelativePath = null,
        string? SourceFileRootMeshCode = null,
        bool Landmark = false,
        DistanceCullingClass? DistanceCullingClass = null)
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
            SourceFileRelativePath,
            SourceFileRootMeshCode,
            Landmark,
            DistanceCullingClass)
    {
    }

    public ImportedMesh Mesh => Geometry is TriangleMeshGeometry triangleMesh
        ? triangleMesh.Mesh
        : throw new InvalidOperationException("This imported city object does not use triangle mesh geometry.");
}
