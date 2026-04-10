#pragma warning disable IDE0032

namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record PlateauSourceDataset
{
    private IReadOnlyList<string> packageNames = Array.Empty<string>();
    private IReadOnlyList<string> sourceFiles = Array.Empty<string>();
    private IReadOnlyList<TerrainTextureOverlay> terrainTextureOverlays = Array.Empty<TerrainTextureOverlay>();
    private IReadOnlyList<string>? requestedMeshCodes;

    public PlateauSourceDataset(
        IReadOnlyList<string> PackageNames,
        IReadOnlyList<string> SourceFiles,
        IReadOnlyList<TerrainTextureOverlay> TerrainTextureOverlays,
        IReadOnlyList<string>? RequestedMeshCodes = null)
    {
        this.PackageNames = PackageNames;
        this.SourceFiles = SourceFiles;
        this.TerrainTextureOverlays = TerrainTextureOverlays;
        this.RequestedMeshCodes = RequestedMeshCodes;
    }

    public IReadOnlyList<string> PackageNames
    {
        get => packageNames;
        init => packageNames = CollectionCopy.List(value, nameof(PackageNames));
    }

    public IReadOnlyList<string> SourceFiles
    {
        get => sourceFiles;
        init => sourceFiles = CollectionCopy.List(value, nameof(SourceFiles));
    }

    public IReadOnlyList<TerrainTextureOverlay> TerrainTextureOverlays
    {
        get => terrainTextureOverlays;
        init => terrainTextureOverlays = CollectionCopy.List(value, nameof(TerrainTextureOverlays));
    }

    public IReadOnlyList<string>? RequestedMeshCodes
    {
        get => requestedMeshCodes;
        init => requestedMeshCodes = CollectionCopy.ListOrNull(value);
    }

    public void Deconstruct(
        out IReadOnlyList<string> PackageNames,
        out IReadOnlyList<string> SourceFiles,
        out IReadOnlyList<TerrainTextureOverlay> TerrainTextureOverlays,
        out IReadOnlyList<string>? RequestedMeshCodes)
    {
        PackageNames = this.PackageNames;
        SourceFiles = this.SourceFiles;
        TerrainTextureOverlays = this.TerrainTextureOverlays;
        RequestedMeshCodes = this.RequestedMeshCodes;
    }
}

#pragma warning restore IDE0032
