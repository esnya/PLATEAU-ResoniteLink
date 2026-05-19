using System;
using System.Collections.Generic;

using System.Text.RegularExpressions;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

public sealed record ValidatedPlateauImportRequest(
    string Dataset,
    string MeshCode,
    Regex MeshCodePattern,
    ValidatedDatasetLocation CityGmlSource,
    ValidatedDatasetLocation? DemTextureSource = null,
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

    public string? CityGmlLocalSourcePath => CityGmlSource is ValidatedLocalDatasetLocation localSource ? localSource.LocalSourcePath : null;

    public Uri? CityGmlServerUri => CityGmlSource is ValidatedRemoteDatasetLocation remoteSource ? remoteSource.ServerUri : null;

    public DatasetSourceKind? DemTextureSourceKind => DemTextureSource?.SourceKind;

    public string? DemTextureLocalSourcePath => DemTextureSource is ValidatedLocalDatasetLocation localSource ? localSource.LocalSourcePath : null;

    public Uri? DemTextureServerUri => DemTextureSource is ValidatedRemoteDatasetLocation remoteSource ? remoteSource.ServerUri : null;

    public PlateauImportRequest ToImportRequest()
    {
        DatasetLocation rawCityGmlSource = CityGmlSource switch
        {
            ValidatedLocalDatasetLocation localSource => new LocalDatasetLocation(localSource.LocalSourcePath),
            ValidatedRemoteDatasetLocation remoteSource => new RemoteDatasetLocation(remoteSource.ServerUri),
            _ => throw new InvalidOperationException($"Unsupported validated CityGML source kind '{CityGmlSourceKind}'."),
        };
        DatasetLocation? rawDemTextureSource = DemTextureSource switch
        {
            null => null,
            ValidatedLocalDatasetLocation localSource => new LocalDatasetLocation(localSource.LocalSourcePath),
            ValidatedRemoteDatasetLocation remoteSource => new RemoteDatasetLocation(remoteSource.ServerUri),
            _ => throw new InvalidOperationException($"Unsupported validated terrain texture source kind '{DemTextureSourceKind}'."),
        };

        return new PlateauImportRequest(
            Dataset,
            MeshCode,
            rawCityGmlSource,
            rawDemTextureSource,
            PackageNames,
            GlobalExcludeLodLevels,
            ExcludeLodLevelsByPackage,
            PackagePatterns,
            IncludeMarkingAlways,
            TerrainMeshMode,
            TerrainGridMetersPerVertex,
            TerrainGridMaxResolution);
    }
}
