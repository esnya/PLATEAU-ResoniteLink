using System.Collections.Generic;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

public sealed class LocalCityGmlDocumentSet
{
    internal LocalCityGmlDocumentSet(
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<string> relativeSourceFiles,
        IReadOnlyList<string> packageNames,
        IReadOnlyList<TerrainTextureOverlay> terrainTextureOverlays,
        IReadOnlyList<string> requestedMeshCodes)
    {
        DatasetSource = datasetSource;
        RelativeSourceFiles = relativeSourceFiles;
        PackageNames = packageNames;
        TerrainTextureOverlays = terrainTextureOverlays;
        SelectedMeshCodes = requestedMeshCodes;
    }

    public IPlateauDatasetContentSource DatasetSource { get; }

    public IReadOnlyList<string> RelativeSourceFiles { get; }

    public IReadOnlyList<string> PackageNames { get; }

    public IReadOnlyList<TerrainTextureOverlay> TerrainTextureOverlays { get; }

    public IReadOnlyList<string> SelectedMeshCodes { get; }

    internal LocalCityGmlDocumentSet WithTerrainTextureOverlays(
        IReadOnlyList<TerrainTextureOverlay> terrainTextureOverlays)
    {
        return new LocalCityGmlDocumentSet(
            DatasetSource,
            RelativeSourceFiles,
            PackageNames,
            terrainTextureOverlays,
            SelectedMeshCodes);
    }
}
