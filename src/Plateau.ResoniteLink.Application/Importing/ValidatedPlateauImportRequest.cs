using System.Text.RegularExpressions;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public sealed record ValidatedPlateauImportRequest(
    string Dataset,
    string MeshCode,
    Regex MeshCodePattern,
    ValidatedPlateauImportSource Source,
    IReadOnlyList<string>? PackageNames = null,
    IReadOnlySet<int>? GlobalExcludeLodLevels = null,
    IReadOnlyDictionary<string, IReadOnlySet<int>>? ExcludeLodLevelsByPackage = null,
    IReadOnlyDictionary<string, string>? PackagePatterns = null,
    bool IncludeMarkingAlways = true,
    DemTerrainMode DemTerrainMode = DemTerrainMode.Mesh,
    double DemHeightmapMetersPerVertex = 2.0,
    int DemHeightmapMaxResolution = 1024)
{
    public DatasetSourceKind SourceKind => Source.SourceKind;

    public string? LocalSourcePath => Source is ValidatedPlateauLocalImportSource localSource ? localSource.LocalSourcePath : null;

    public Uri? ServerUri => Source is ValidatedPlateauRemoteImportSource remoteSource ? remoteSource.ServerUri : null;

    public PlateauImportRequest ToImportRequest()
    {
        PlateauImportSource rawSource = Source switch
        {
            ValidatedPlateauLocalImportSource localSource => new PlateauLocalImportSource(localSource.LocalSourcePath),
            ValidatedPlateauRemoteImportSource remoteSource => new PlateauRemoteImportSource(remoteSource.ServerUri),
            _ => throw new InvalidOperationException($"Unsupported validated source kind '{SourceKind}'."),
        };

        return new PlateauImportRequest(
            Dataset,
            MeshCode,
            rawSource,
            PackageNames,
            GlobalExcludeLodLevels,
            ExcludeLodLevelsByPackage,
            PackagePatterns,
            IncludeMarkingAlways,
            DemTerrainMode,
            DemHeightmapMetersPerVertex,
            DemHeightmapMaxResolution);
    }
}
