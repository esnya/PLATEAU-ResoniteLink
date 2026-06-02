using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

public sealed record ResolvedLocalPlateauImportRequest
{
    private readonly ValidatedLocalDatasetLocation? demTextureSource;

    public ResolvedLocalPlateauImportRequest(
        string Dataset,
        string MeshCode,
        string CityGmlLocalSourcePath,
        LocalDatasetLocation? DemTextureSource = null,
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

    public LocalDatasetLocation? DemTextureSource => demTextureSource is null
        ? null
        : new LocalDatasetLocation(demTextureSource.LocalSourcePath);

    public string? DemTextureLocalSourcePath => demTextureSource?.LocalSourcePath;

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
        string workRoot,
        ValidatedLocalDatasetLocation cityGmlSource,
        ValidatedLocalDatasetLocation? demTextureSource)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);
        ArgumentNullException.ThrowIfNull(cityGmlSource);

        ValidateResolvedSource(request.CityGmlSource, cityGmlSource, workRoot, "source-archive", nameof(cityGmlSource));
        ValidateResolvedOptionalSource(request.DemTextureSource, demTextureSource, workRoot, "source-ortho", nameof(demTextureSource));

        return new ResolvedLocalPlateauImportRequest(request, cityGmlSource, demTextureSource);
    }

    private static void ValidateResolvedOptionalSource(
        ValidatedDatasetLocation? requestedSource,
        ValidatedLocalDatasetLocation? resolvedSource,
        string workRoot,
        string remotePathPrefix,
        string parameterName)
    {
        if (requestedSource is null || resolvedSource is null)
        {
            if (requestedSource is not null || resolvedSource is not null)
            {
                throw new ArgumentException(
                    "Resolved local source presence must match the requested source presence.",
                    parameterName);
            }

            return;
        }

        ValidateResolvedSource(requestedSource, resolvedSource, workRoot, remotePathPrefix, parameterName);
    }

    private static void ValidateResolvedSource(
        ValidatedDatasetLocation requestedSource,
        ValidatedLocalDatasetLocation resolvedSource,
        string workRoot,
        string remotePathPrefix,
        string parameterName)
    {
        switch (requestedSource)
        {
            case ValidatedLocalDatasetLocation requestedLocal
                when string.Equals(requestedLocal.LocalSourcePath, resolvedSource.LocalSourcePath, StringComparison.Ordinal):
                return;
            case ValidatedRemoteDatasetLocation requestedRemote
                when RemoteDatasetResourceLayout.MatchesRemoteResourcePath(
                    workRoot,
                    requestedRemote.ServerUri,
                    remotePathPrefix,
                    resolvedSource.LocalSourcePath):
                return;
            default:
                throw new ArgumentException(
                    "Resolved local source must match the requested local source or the expected remote materialization path.",
                    parameterName);
        }
    }

    private static ValidatedPlateauImportRequest CreateResolvedRequest(
        string dataset,
        string meshCode,
        ValidatedLocalDatasetLocation cityGmlSource,
        ValidatedLocalDatasetLocation? demTextureSource,
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
        LocalDatasetLocation? source,
        string parameterName)
    {
        return source is null
            ? null
            : new ValidatedLocalDatasetLocation(RequireNonEmpty(source.LocalSourcePath, parameterName));
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
        string? trimmedValue = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmedValue)
            ? throw new ArgumentException("The value must not be empty.", parameterName)
            : trimmedValue;
    }
}
