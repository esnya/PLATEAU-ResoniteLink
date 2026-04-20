namespace PlateauResoniteLink.Domain.Importing;

public sealed record PlateauImportRequest(
    string Dataset,
    string MeshCode,
    PlateauImportSource Source,
    IReadOnlyList<string>? PackageNames = null,
    IReadOnlySet<int>? GlobalExcludeLodLevels = null,
    IReadOnlyDictionary<string, IReadOnlySet<int>>? ExcludeLodLevelsByPackage = null,
    IReadOnlyDictionary<string, string>? PackagePatterns = null,
    bool IncludeMarkingAlways = true,
    DemTerrainMode DemTerrainMode = DemTerrainMode.Mesh,
    double DemHeightmapMetersPerVertex = 2.0,
    int DemHeightmapMaxResolution = 1024)
{
    public PlateauImportRequest(
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
        int DemHeightmapMaxResolution = 1024)
        : this(
            Dataset,
            MeshCode,
            PlateauImportSource.FromLegacy(SourceKind, LocalSourcePath, ServerUri),
            PackageNames,
            GlobalExcludeLodLevels,
            ExcludeLodLevelsByPackage,
            PackagePatterns,
            IncludeMarkingAlways,
            DemTerrainMode,
            DemHeightmapMetersPerVertex,
            DemHeightmapMaxResolution)
    {
    }

    public DatasetSourceKind SourceKind => Source.SourceKind;

    public string? LocalSourcePath => Source is PlateauLocalImportSource localSource ? localSource.LocalSourcePath : null;

    public Uri? ServerUri => Source is PlateauRemoteImportSource remoteSource ? remoteSource.ServerUri : null;
}
