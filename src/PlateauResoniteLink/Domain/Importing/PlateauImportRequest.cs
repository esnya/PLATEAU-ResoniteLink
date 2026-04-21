using System;
using System.Collections.Generic;

namespace PlateauResoniteLink.Domain.Importing;

public sealed record PlateauImportRequest(
    string Dataset,
    string MeshCode,
    DatasetLocation Source,
    DatasetLocation? DemTextureSource = null,
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
        DatasetSourceKind? DemTextureSourceKind = null,
        string? DemTextureLocalSourcePath = null,
        Uri? DemTextureServerUri = null,
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
            DatasetLocation.FromLegacy(SourceKind, LocalSourcePath, ServerUri),
            DemTextureSourceKind is null
                ? null
                : DatasetLocation.FromLegacy(DemTextureSourceKind.Value, DemTextureLocalSourcePath, DemTextureServerUri),
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

    public string? LocalSourcePath => Source is LocalDatasetLocation localSource ? localSource.LocalSourcePath : null;

    public Uri? ServerUri => Source is RemoteDatasetLocation remoteSource ? remoteSource.ServerUri : null;

    public DatasetSourceKind? DemTextureSourceKind => DemTextureSource?.SourceKind;

    public string? DemTextureLocalSourcePath => DemTextureSource is LocalDatasetLocation localSource ? localSource.LocalSourcePath : null;

    public Uri? DemTextureServerUri => DemTextureSource is RemoteDatasetLocation remoteSource ? remoteSource.ServerUri : null;
}
