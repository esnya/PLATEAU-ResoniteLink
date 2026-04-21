using System;
using System.Collections.Generic;

using System.Text.RegularExpressions;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

public sealed record ValidatedPlateauImportRequest(
    string Dataset,
    string MeshCode,
    Regex MeshCodePattern,
    ValidatedDatasetLocation Source,
    ValidatedDatasetLocation? DemTextureSource = null,
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

    public string? LocalSourcePath => Source is ValidatedLocalDatasetLocation localSource ? localSource.LocalSourcePath : null;

    public Uri? ServerUri => Source is ValidatedRemoteDatasetLocation remoteSource ? remoteSource.ServerUri : null;

    public DatasetSourceKind? DemTextureSourceKind => DemTextureSource?.SourceKind;

    public string? DemTextureLocalSourcePath => DemTextureSource is ValidatedLocalDatasetLocation localSource ? localSource.LocalSourcePath : null;

    public Uri? DemTextureServerUri => DemTextureSource is ValidatedRemoteDatasetLocation remoteSource ? remoteSource.ServerUri : null;

    public PlateauImportRequest ToImportRequest()
    {
        DatasetLocation rawSource = Source switch
        {
            ValidatedLocalDatasetLocation localSource => new LocalDatasetLocation(localSource.LocalSourcePath),
            ValidatedRemoteDatasetLocation remoteSource => new RemoteDatasetLocation(remoteSource.ServerUri),
            _ => throw new InvalidOperationException($"Unsupported validated source kind '{SourceKind}'."),
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
            rawSource,
            rawDemTextureSource,
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
