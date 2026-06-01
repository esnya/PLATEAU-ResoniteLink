using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

public sealed record ResolvedLocalPlateauImportRequest
{
    private readonly ValidatedDatasetLocation? demTextureSource;

    public ResolvedLocalPlateauImportRequest(
        string Dataset,
        string MeshCode,
        string CityGmlLocalSourcePath,
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
        ValidatedLocalDatasetLocation cityGmlSource = new(RequireNonEmpty(CityGmlLocalSourcePath, nameof(CityGmlLocalSourcePath)));
        ValidatedLocalDatasetLocation? validatedDemTextureSource = ToResolvedDemTextureSource(DemTextureSource, nameof(DemTextureSource));
        ValidatedRequest = CreateResolvedRequest(
            Dataset,
            MeshCode,
            cityGmlSource,
            validatedDemTextureSource,
            PackageNames,
            GlobalExcludeLodLevels,
            ExcludeLodLevelsByPackage,
            PackagePatterns,
            IncludeMarkingAlways,
            TerrainMeshMode,
            TerrainGridMetersPerVertex,
            TerrainGridMaxResolution);
        CityGmlSource = cityGmlSource;
        demTextureSource = validatedDemTextureSource;
    }

    private ResolvedLocalPlateauImportRequest(
        ValidatedPlateauImportRequest request,
        ValidatedLocalDatasetLocation cityGmlSource,
        ValidatedDatasetLocation? demTextureSource)
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

    internal static ResolvedLocalPlateauImportRequest Create(
        ValidatedPlateauImportRequest request,
        ValidatedLocalDatasetLocation cityGmlSource,
        ValidatedDatasetLocation? demTextureSource)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(cityGmlSource);
        if (demTextureSource is ValidatedRemoteDatasetLocation)
        {
            throw new ArgumentException("Resolved local import requests require DEM texture sources to be local when specified.", nameof(demTextureSource));
        }

        return new ResolvedLocalPlateauImportRequest(request, cityGmlSource, demTextureSource);
    }

    private static ValidatedPlateauImportRequest CreateResolvedRequest(
        string dataset,
        string meshCode,
        ValidatedLocalDatasetLocation cityGmlSource,
        ValidatedDatasetLocation? demTextureSource,
        IReadOnlyList<string>? packageNames,
        IReadOnlySet<int>? globalExcludeLodLevels,
        IReadOnlyDictionary<string, IReadOnlySet<int>>? excludeLodLevelsByPackage,
        IReadOnlyDictionary<string, string>? packagePatterns,
        bool includeMarkingAlways,
        TerrainMeshMode terrainMeshMode,
        double terrainGridMetersPerVertex,
        int terrainGridMaxResolution)
    {
        string requiredDataset = RequireNonEmpty(dataset, nameof(dataset));
        string requiredMeshCode = RequireNonEmpty(meshCode, nameof(meshCode));
        string[]? normalizedPackageNames = NormalizePackageNames(packageNames);
        Dictionary<string, IReadOnlySet<int>>? normalizedPackageExclusions = NormalizePackageExclusionMap(excludeLodLevelsByPackage);
        Dictionary<string, string>? normalizedPackagePatterns = NormalizePackagePatternMap(packagePatterns);
        if (!MeshCodeRequestSyntax.TryCreateSelectionRegex(requiredMeshCode, out Regex? meshCodePattern, out string? meshCodeError))
        {
            throw new ArgumentException(meshCodeError, nameof(meshCode));
        }

        meshCodePattern ??= new Regex(
            $@"\A(?:{Regex.Escape(requiredMeshCode)})\z",
            RegexOptions.Compiled | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        return new ValidatedPlateauImportRequest(
            requiredDataset,
            requiredMeshCode,
            meshCodePattern,
            cityGmlSource,
            demTextureSource,
            normalizedPackageNames,
            globalExcludeLodLevels,
            normalizedPackageExclusions,
            normalizedPackagePatterns,
            includeMarkingAlways,
            terrainMeshMode,
            terrainGridMetersPerVertex,
            terrainGridMaxResolution);
    }

    private static ValidatedLocalDatasetLocation? ToResolvedDemTextureSource(
        DatasetLocation? source,
        string parameterName)
    {
        return source switch
        {
            null => null,
            LocalDatasetLocation localSource => new ValidatedLocalDatasetLocation(RequireNonEmpty(localSource.LocalSourcePath, parameterName)),
            RemoteDatasetLocation => throw new ArgumentException("Resolved local import requests require DEM texture sources to be local when specified.", parameterName),
            _ => throw new ArgumentException("Unsupported dataset source location.", parameterName),
        };
    }

    private static string[]? NormalizePackageNames(IReadOnlyList<string>? packageNames)
    {
        if (packageNames is null)
        {
            return null;
        }

        if (packageNames.Count == 0)
        {
            throw new ArgumentException("At least one package name is required when packages are specified.", nameof(packageNames));
        }

        return PlateauPackageCatalog.NormalizeRequestedPackageNames(packageNames);
    }

    private static Dictionary<string, IReadOnlySet<int>>? NormalizePackageExclusionMap(
        IReadOnlyDictionary<string, IReadOnlySet<int>>? exclusionsByPackage)
    {
        if (exclusionsByPackage is null)
        {
            return null;
        }

        Dictionary<string, IReadOnlySet<int>> normalized = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string packageName, IReadOnlySet<int> excludedLods) in exclusionsByPackage)
        {
            string normalizedPackageName = NormalizePackageMapKey(packageName, nameof(exclusionsByPackage));
            if (!normalized.TryAdd(normalizedPackageName, excludedLods))
            {
                throw new ArgumentException(
                    $"The {nameof(exclusionsByPackage)} value contains duplicate package keys after normalization: {normalizedPackageName}.",
                    nameof(exclusionsByPackage));
            }
        }

        return normalized;
    }

    private static Dictionary<string, string>? NormalizePackagePatternMap(
        IReadOnlyDictionary<string, string>? patternsByPackage)
    {
        if (patternsByPackage is null)
        {
            return null;
        }

        Dictionary<string, string> normalized = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string packageName, string pattern) in patternsByPackage)
        {
            string normalizedPackageName = NormalizePackageMapKey(packageName, nameof(patternsByPackage));
            if (!normalized.TryAdd(normalizedPackageName, pattern))
            {
                throw new ArgumentException(
                    $"The {nameof(patternsByPackage)} value contains duplicate package keys after normalization: {normalizedPackageName}.",
                    nameof(patternsByPackage));
            }
        }

        return normalized;
    }

    private static string NormalizePackageMapKey(string packageName, string parameterName)
    {
        if (!PlateauPackageCatalog.TryNormalizePackageName(packageName, out string normalizedPackageName))
        {
            throw new ArgumentException(
                $"Unsupported package '{packageName}'. Supported packages: {string.Join(", ", PlateauPackageCatalog.SupportedPackageNames)}.",
                parameterName);
        }

        return normalizedPackageName;
    }

    private static string RequireNonEmpty(string? value, string parameterName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The value must not be empty.", parameterName)
            : value;
    }
}
