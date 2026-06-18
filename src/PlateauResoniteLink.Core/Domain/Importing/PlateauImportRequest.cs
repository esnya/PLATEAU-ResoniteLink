using System;
using System.Collections.Generic;

namespace PlateauResoniteLink.Core.Domain.Importing;

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
    int TerrainGridMaxResolution = 1024,
    bool ExcludeGsiTerrainTiles = false)
{
#pragma warning disable IDE0032 // Backing field keeps with-expressions from assigning a null CityGmlSource.
    private DatasetLocation cityGmlSource =
        CityGmlSource ?? throw new ArgumentNullException(nameof(CityGmlSource));
#pragma warning restore IDE0032

    public DatasetLocation CityGmlSource
    {
        get => cityGmlSource;
        init => cityGmlSource = value ?? throw new ArgumentNullException(nameof(CityGmlSource));
    }

    public DatasetSourceKind CityGmlSourceKind => CityGmlSource.SourceKind;

    public string? CityGmlLocalSourcePath => CityGmlSource is LocalDatasetLocation localSource ? localSource.LocalSourcePath : null;

    public Uri? CityGmlServerUri => CityGmlSource is RemoteDatasetLocation remoteSource ? remoteSource.ServerUri : null;

    public DatasetSourceKind? DemTextureSourceKind => DemTextureSource?.SourceKind;

    public string? DemTextureLocalSourcePath => DemTextureSource is LocalDatasetLocation localSource ? localSource.LocalSourcePath : null;

    public Uri? DemTextureServerUri => DemTextureSource is RemoteDatasetLocation remoteSource ? remoteSource.ServerUri : null;
}
