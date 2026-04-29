using System;
using System.Collections.Generic;

namespace PlateauResoniteLink.Application.Importing;

public sealed record ImportedCityObject(
    string ObjectKey,
    string DisplayName,
    string PackageName,
    string ActualMeshCode,
    DetailEntry DetailEntry,
    DetailEntry FinestDetailGroup,
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
        DetailEntry DetailEntry,
        DetailEntry FinestDetailGroup,
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
            DetailEntry,
            FinestDetailGroup,
            Transform,
            new TriangleMeshGeometry(Mesh),
            Materials,
            CollisionEnabled,
            SourceFileRelativePath)
    {
    }

    public ImportedCityObject(
        string ObjectKey,
        string DisplayName,
        string PackageName,
        string ActualMeshCode,
        DetailEntry DetailEntry,
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
            DetailEntry,
            DetailEntry,
            Transform,
            Mesh,
            Materials,
            CollisionEnabled,
            SourceFileRelativePath)
    {
    }

    public ImportedCityObject(
        string ObjectKey,
        string DisplayName,
        string PackageName,
        string ActualMeshCode,
        DetailEntry DetailEntry,
        Transform3D Transform,
        ConstructionGeometry Geometry,
        IReadOnlyList<MaterialBinding> Materials,
        bool CollisionEnabled = true,
        string? SourceFileRelativePath = null)
        : this(
            ObjectKey,
            DisplayName,
            PackageName,
            ActualMeshCode,
            DetailEntry,
            DetailEntry,
            Transform,
            Geometry,
            Materials,
            CollisionEnabled,
            SourceFileRelativePath)
    {
    }

    public ImportedCityObject(
        string ObjectKey,
        string DisplayName,
        string PackageName,
        string ActualMeshCode,
        int? sourceRepresentationIndex,
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
            DetailEntry.FromSourceRepresentationIndex(sourceRepresentationIndex),
            DetailEntry.FromSourceRepresentationIndex(sourceRepresentationIndex),
            Transform,
            Mesh,
            Materials,
            CollisionEnabled,
            SourceFileRelativePath)
    {
    }

    public ImportedCityObject(
        string ObjectKey,
        string DisplayName,
        string PackageName,
        string ActualMeshCode,
        int? sourceRepresentationIndex,
        Transform3D Transform,
        ConstructionGeometry Geometry,
        IReadOnlyList<MaterialBinding> Materials,
        bool CollisionEnabled = true,
        string? SourceFileRelativePath = null)
        : this(
            ObjectKey,
            DisplayName,
            PackageName,
            ActualMeshCode,
            DetailEntry.FromSourceRepresentationIndex(sourceRepresentationIndex),
            DetailEntry.FromSourceRepresentationIndex(sourceRepresentationIndex),
            Transform,
            Geometry,
            Materials,
            CollisionEnabled,
            SourceFileRelativePath)
    {
    }

    public ImportedMesh Mesh => Geometry is TriangleMeshGeometry triangleMesh
        ? triangleMesh.Mesh
        : throw new InvalidOperationException("This imported city object does not use triangle mesh geometry.");
}
