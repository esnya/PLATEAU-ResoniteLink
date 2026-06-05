using System;
using System.Collections.Generic;

namespace PlateauResoniteLink.Domain.Importing;

public sealed record PlateauImportRequest(
    string Dataset,
    string MeshCode,
    DatasetLocation CityGmlSource,
    DatasetLocation? DemTextureSource = null,
    IReadOnlyList<string>? PackageNames = null,
    IReadOnlySet<int>? GlobalExcludeLodLevels = null,
    IReadOnlyDictionary<string, IReadOnlySet<int>>? ExcludeLodLevelsByPackage = null,
    IReadOnlyDictionary<string, string>? PackagePatterns = null,
    bool IncludeMarkingAlways = true,
    TerrainMeshMode TerrainMeshMode = TerrainMeshMode.Static,
    double TerrainGridMetersPerVertex = 2.0,
    int TerrainGridMaxResolution = 1024)
{
    public DatasetSourceKind CityGmlSourceKind => CityGmlSource.SourceKind;

    public string? CityGmlLocalSourcePath => CityGmlSource is LocalDatasetLocation localSource ? localSource.LocalSourcePath : null;

    public Uri? CityGmlServerUri => CityGmlSource is RemoteDatasetLocation remoteSource ? remoteSource.ServerUri : null;

    public DatasetSourceKind? DemTextureSourceKind => DemTextureSource?.SourceKind;

    public string? DemTextureLocalSourcePath => DemTextureSource is LocalDatasetLocation localSource ? localSource.LocalSourcePath : null;

    public Uri? DemTextureServerUri => DemTextureSource is RemoteDatasetLocation remoteSource ? remoteSource.ServerUri : null;
}
