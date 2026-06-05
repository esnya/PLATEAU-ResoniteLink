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
#pragma warning disable IDE0032 // Backing fields keep with-expressions inside validated invariants.
    private string dataset = RequireNonWhiteSpace(Dataset, nameof(Dataset));
    private string meshCode = RequireNonWhiteSpace(MeshCode, nameof(MeshCode));
    private Regex meshCodePattern = MeshCodePattern ?? throw new ArgumentNullException(nameof(MeshCodePattern));
    private ValidatedDatasetLocation cityGmlSource =
        CityGmlSource ?? throw new ArgumentNullException(nameof(CityGmlSource));
    private double terrainGridMetersPerVertex = RequirePositive(
        TerrainGridMetersPerVertex,
        nameof(TerrainGridMetersPerVertex));
    private int terrainGridMaxResolution = RequireMinimum(
        TerrainGridMaxResolution,
        2,
        nameof(TerrainGridMaxResolution));
#pragma warning restore IDE0032

    public string Dataset
    {
        get => dataset;
        init => dataset = RequireNonWhiteSpace(value, nameof(Dataset));
    }

    public string MeshCode
    {
        get => meshCode;
        init => meshCode = RequireNonWhiteSpace(value, nameof(MeshCode));
    }

    public Regex MeshCodePattern
    {
        get => meshCodePattern;
        init => meshCodePattern = value ?? throw new ArgumentNullException(nameof(MeshCodePattern));
    }

    public ValidatedDatasetLocation CityGmlSource
    {
        get => cityGmlSource;
        init => cityGmlSource = value ?? throw new ArgumentNullException(nameof(CityGmlSource));
    }

    public double TerrainGridMetersPerVertex
    {
        get => terrainGridMetersPerVertex;
        init => terrainGridMetersPerVertex = RequirePositive(value, nameof(TerrainGridMetersPerVertex));
    }

    public int TerrainGridMaxResolution
    {
        get => terrainGridMaxResolution;
        init => terrainGridMaxResolution = RequireMinimum(value, 2, nameof(TerrainGridMaxResolution));
    }

    public DatasetSourceKind CityGmlSourceKind => CityGmlSource.SourceKind;

    public string? CityGmlLocalSourcePath => CityGmlSource is ValidatedLocalDatasetLocation localSource ? localSource.LocalSourcePath : null;

    public Uri? CityGmlServerUri => CityGmlSource is ValidatedRemoteDatasetLocation remoteSource ? remoteSource.ServerUri : null;

    public DatasetSourceKind? DemTextureSourceKind => DemTextureSource?.SourceKind;

    public string? DemTextureLocalSourcePath => DemTextureSource is ValidatedLocalDatasetLocation localSource ? localSource.LocalSourcePath : null;

    public Uri? DemTextureServerUri => DemTextureSource is ValidatedRemoteDatasetLocation remoteSource ? remoteSource.ServerUri : null;

    public PlateauImportRequest ToImportRequest()
    {
        DatasetLocation rawCityGmlSource = CityGmlSource.ToDatasetLocation();
        DatasetLocation? rawDemTextureSource = DemTextureSource?.ToDatasetLocation();

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

    private static string RequireNonWhiteSpace(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }

    private static double RequirePositive(double value, string parameterName)
    {
        return double.IsFinite(value) && value > 0
            ? value
            : throw new ArgumentOutOfRangeException(parameterName, value, "The value must be finite and greater than zero.");
    }

    private static int RequireMinimum(int value, int minimum, string parameterName)
    {
        return value >= minimum
            ? value
            : throw new ArgumentOutOfRangeException(parameterName, value, $"The value must be at least {minimum}.");
    }
}
