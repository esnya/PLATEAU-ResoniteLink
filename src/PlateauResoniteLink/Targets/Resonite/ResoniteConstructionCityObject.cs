using System.Collections.Generic;

namespace PlateauResoniteLink.Targets.Resonite;

public sealed record ResoniteConstructionCityObject(
    string SlotKey,
    string DisplayName,
    string PackageName,
    string ActualMeshCode,
    RenderStage RenderStage,
    RenderStage FinestRenderStageGroup,
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
        RenderStage RenderStage,
        RenderStage FinestRenderStageGroup,
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
            RenderStage,
            FinestRenderStageGroup,
            Transform,
            new ResoniteTriangleMeshGeometry(Mesh),
            Materials,
            CollisionEnabled,
            SourceFileRelativePath)
    {
    }

    public ResoniteConstructionCityObject(
        string SlotKey,
        string DisplayName,
        string PackageName,
        string ActualMeshCode,
        int? sourceRepresentationIndex,
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
            RenderStage.FromSourceRepresentationIndex(sourceRepresentationIndex),
            RenderStage.FromSourceRepresentationIndex(sourceRepresentationIndex),
            Transform,
            Mesh,
            Materials,
            CollisionEnabled,
            SourceFileRelativePath)
    {
    }

    public ResoniteConstructionCityObject(
        string SlotKey,
        string DisplayName,
        string PackageName,
        string ActualMeshCode,
        int? sourceRepresentationIndex,
        ResoniteTransform Transform,
        ResoniteConstructionGeometry Geometry,
        IReadOnlyList<ResoniteMaterialBinding> Materials,
        bool CollisionEnabled = true,
        string? SourceFileRelativePath = null)
        : this(
            SlotKey,
            DisplayName,
            PackageName,
            ActualMeshCode,
            RenderStage.FromSourceRepresentationIndex(sourceRepresentationIndex),
            RenderStage.FromSourceRepresentationIndex(sourceRepresentationIndex),
            Transform,
            Geometry,
            Materials,
            CollisionEnabled,
            SourceFileRelativePath)
    {
    }

    public ResoniteImportedMesh Mesh => Geometry is ResoniteTriangleMeshGeometry triangleMesh
        ? triangleMesh.Mesh
        : throw new System.InvalidOperationException("This city object does not use triangle mesh geometry.");
}
