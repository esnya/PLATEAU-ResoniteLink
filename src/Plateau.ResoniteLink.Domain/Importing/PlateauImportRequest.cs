namespace Plateau.ResoniteLink.Domain.Importing;

public sealed record PlateauImportRequest(
    string Dataset,
    string MeshCode,
    DatasetSourceKind SourceKind,
    string? LocalSourcePath,
    Uri? ServerUri,
    IReadOnlyList<string>? PackageNames = null,
    IReadOnlySet<int>? GlobalExcludeLodLevels = null,
    IReadOnlyDictionary<string, IReadOnlySet<int>>? ExcludeLodLevelsByPackage = null,
    IReadOnlyDictionary<string, string>? PackagePatterns = null,
    bool IncludeMarkingAlways = true,
    DemTerrainMode DemTerrainMode = DemTerrainMode.Mesh,
    double DemHeightmapMetersPerVertex = 2.0,
    int DemHeightmapMaxResolution = 1024);
