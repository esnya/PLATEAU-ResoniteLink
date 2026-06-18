using System.Collections.Generic;

using PlateauResoniteLink.Core.Domain.Importing;

namespace PlateauResoniteLink.Plateau.Application.Importing;

internal sealed class ImportedSceneSourceDataset
{
    internal ImportedSceneSourceDataset(
        IReadOnlyList<string> relativeSourceFiles,
        IReadOnlyList<string> packageNames,
        IReadOnlyList<TerrainTextureOverlay> terrainTextureOverlays,
        IReadOnlyList<string> requestedMeshCodes)
    {
        RelativeSourceFiles = relativeSourceFiles;
        PackageNames = packageNames;
        TerrainTextureOverlays = terrainTextureOverlays;
        SelectedMeshCodes = requestedMeshCodes;
    }

    public IReadOnlyList<string> RelativeSourceFiles { get; }

    public IReadOnlyList<string> PackageNames { get; }

    public IReadOnlyList<TerrainTextureOverlay> TerrainTextureOverlays { get; }

    public IReadOnlyList<string> SelectedMeshCodes { get; }
}
