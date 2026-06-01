using System;
using System.Collections.Generic;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

public sealed record ResolvedLocalPlateauImportRequest
{
    private readonly ValidatedLocalDatasetLocation? demTextureSource;

    private ResolvedLocalPlateauImportRequest(
        ValidatedPlateauImportRequest request,
        ValidatedLocalDatasetLocation cityGmlSource,
        ValidatedLocalDatasetLocation? demTextureSource)
    {
        ValidatedRequest = request with
        {
            CityGmlSource = cityGmlSource,
            DemTextureSource = demTextureSource,
        };
        CityGmlSource = cityGmlSource;
        this.demTextureSource = demTextureSource;
    }

    internal ValidatedPlateauImportRequest ValidatedRequest { get; }

    internal ValidatedLocalDatasetLocation CityGmlSource { get; }

    public string Dataset => ValidatedRequest.Dataset;

    public string MeshCode => ValidatedRequest.MeshCode;

    public string CityGmlLocalSourcePath => CityGmlSource.LocalSourcePath;

    public DatasetLocation? DemTextureSource => demTextureSource?.ToDatasetLocation();

    public DatasetSourceKind? DemTextureSourceKind => DemTextureSource?.SourceKind;

    public string? DemTextureLocalSourcePath => DemTextureSource is LocalDatasetLocation localSource
        ? localSource.LocalSourcePath
        : null;

    public Uri? DemTextureServerUri => DemTextureSource is RemoteDatasetLocation remoteSource
        ? remoteSource.ServerUri
        : null;

    public IReadOnlyList<string>? PackageNames => ValidatedRequest.PackageNames;

    public IReadOnlySet<int>? GlobalExcludeLodLevels => ValidatedRequest.GlobalExcludeLodLevels;

    public IReadOnlyDictionary<string, IReadOnlySet<int>>? ExcludeLodLevelsByPackage => ValidatedRequest.ExcludeLodLevelsByPackage;

    public IReadOnlyDictionary<string, string>? PackagePatterns => ValidatedRequest.PackagePatterns;

    public bool IncludeMarkingAlways => ValidatedRequest.IncludeMarkingAlways;

    public TerrainMeshMode TerrainMeshMode => ValidatedRequest.TerrainMeshMode;

    public double TerrainGridMetersPerVertex => ValidatedRequest.TerrainGridMetersPerVertex;

    public int TerrainGridMaxResolution => ValidatedRequest.TerrainGridMaxResolution;

    public PlateauImportRequest ToImportRequest()
    {
        return (ValidatedRequest with
        {
            CityGmlSource = CityGmlSource,
            DemTextureSource = demTextureSource,
        }).ToImportRequest();
    }

    public static ResolvedLocalPlateauImportRequest Create(
        ValidatedPlateauImportRequest request,
        ValidatedLocalDatasetLocation cityGmlSource,
        ValidatedLocalDatasetLocation? demTextureSource)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(cityGmlSource);

        return new ResolvedLocalPlateauImportRequest(request, cityGmlSource, demTextureSource);
    }
}
