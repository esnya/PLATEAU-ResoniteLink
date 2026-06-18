using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using System.Text.RegularExpressions;

using PlateauResoniteLink.Domain.Importing;
namespace PlateauResoniteLink.Application.Importing;

public static class PlateauImportRequestValidator
{
    private static readonly string[] SupportedRemoteArchiveExtensions = [".zip", ".7z"];
    private static readonly string[] SupportedTerrainTextureExtensions = [".zip", ".7z", ".tif", ".tiff"];

    public static IReadOnlyList<string> Validate(PlateauImportRequest request)
    {
        _ = TryNormalizeAndValidate(request, out _, out IReadOnlyList<string> errors);
        return errors;
    }

    public static bool TryNormalizeAndValidate(
        PlateauImportRequest request,
        out ValidatedPlateauImportRequest? validatedRequest,
        out IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(request);

        PlateauImportRequest normalizedRequest = NormalizeRawRequest(request);
        List<string> validationErrors = [];
        Regex? meshCodePattern = null;
        IReadOnlyList<string>? normalizedPackageNames = null;
        IReadOnlyDictionary<string, IReadOnlySet<int>>? normalizedPackageExclusions = null;
        IReadOnlyDictionary<string, string>? normalizedPackagePatterns = null;
        ValidatedDatasetLocation? validatedCityGmlSource = null;
        ValidatedDatasetLocation? validatedDemTextureSource = null;

        if (string.IsNullOrWhiteSpace(normalizedRequest.Dataset))
        {
            validationErrors.Add("The dataset value is required.");
        }

        if (string.IsNullOrWhiteSpace(normalizedRequest.MeshCode))
        {
            validationErrors.Add("The mesh-code value is required.");
        }
        else if (!MeshCodeRequestSyntax.TryCreateSelectionRegex(normalizedRequest.MeshCode, out meshCodePattern, out string? meshCodeError))
        {
            validationErrors.Add(meshCodeError!);
        }
        else if (meshCodePattern is null)
        {
            meshCodePattern = new Regex(
                $@"\A(?:{Regex.Escape(normalizedRequest.MeshCode)})\z",
                RegexOptions.Compiled | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
        }

        if (normalizedRequest.PackageNames is not null)
        {
            if (normalizedRequest.PackageNames.Count == 0)
            {
                validationErrors.Add("At least one package name is required when packages are specified.");
            }
            else
            {
                string[] unsupportedPackageNames = normalizedRequest.PackageNames
                    .Select(static packageName => NormalizePackageNameInput(packageName))
                    .Where(static packageName => !PlateauPackageCatalog.TryNormalizePackageName(packageName, out _))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(packageName => packageName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (unsupportedPackageNames.Length > 0)
                {
                    validationErrors.Add(
                        $"Unsupported package name(s): {string.Join(", ", unsupportedPackageNames)}. Supported packages: {string.Join(", ", PlateauPackageCatalog.SupportedPackageNames)}.");
                }
                else
                {
                    normalizedPackageNames = PlateauPackageCatalog.NormalizeRequestedPackageNames(normalizedRequest.PackageNames);
                }
            }
        }

        if (normalizedRequest.ExcludeLodLevelsByPackage is not null)
        {
            AddDuplicatePackageMapKeyError(
                normalizedRequest.ExcludeLodLevelsByPackage.Keys,
                "ExcludeLodLevelsByPackage",
                validationErrors);

            string[] unsupportedPackageNames = normalizedRequest.ExcludeLodLevelsByPackage.Keys
                .Select(static packageName => NormalizePackageNameInput(packageName))
                .Where(static packageName => !PlateauPackageCatalog.TryNormalizePackageName(packageName, out _))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(packageName => packageName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (unsupportedPackageNames.Length > 0)
            {
                validationErrors.Add(
                    $"Unsupported package name(s): {string.Join(", ", unsupportedPackageNames)}. Supported packages: {string.Join(", ", PlateauPackageCatalog.SupportedPackageNames)}.");
            }
            else
            {
                normalizedPackageExclusions = NormalizePackageExclusionMap(normalizedRequest.ExcludeLodLevelsByPackage);
            }
        }

        if (normalizedRequest.PackagePatterns is not null)
        {
            AddDuplicatePackageMapKeyError(
                normalizedRequest.PackagePatterns.Keys,
                "PackagePatterns",
                validationErrors);

            string[] unsupportedPackageNames = normalizedRequest.PackagePatterns.Keys
                .Select(static packageName => NormalizePackageNameInput(packageName))
                .Where(static packageName => !PlateauPackageCatalog.TryNormalizePackageName(packageName, out _))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(packageName => packageName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (unsupportedPackageNames.Length > 0)
            {
                validationErrors.Add(
                    $"Unsupported package name(s): {string.Join(", ", unsupportedPackageNames)}. Supported packages: {string.Join(", ", PlateauPackageCatalog.SupportedPackageNames)}.");
            }
            else
            {
                normalizedPackagePatterns = NormalizePackagePatternMap(normalizedRequest.PackagePatterns);
            }
        }

        switch (normalizedRequest.CityGmlSource)
        {
            case LocalDatasetLocation localSource:
                if (!Directory.Exists(localSource.LocalSourcePath)
                    && !File.Exists(localSource.LocalSourcePath))
                {
                    validationErrors.Add($"The CityGML source path '{localSource.LocalSourcePath}' does not exist.");
                    break;
                }

                if (File.Exists(localSource.LocalSourcePath)
                    && !LooksLikeSupportedLocalDatasetSourcePath(localSource.LocalSourcePath))
                {
                    validationErrors.Add(
                        $"The CityGML source path '{localSource.LocalSourcePath}' must be a dataset directory or a .zip/.7z archive.");
                    break;
                }

                if (!LooksLikeSupportedLocalCityGmlSourcePath(localSource.LocalSourcePath))
                {
                    validationErrors.Add("The --citygml-source value must point to a .zip/.7z archive or directory containing extracted CityGML dataset contents.");
                    break;
                }

                validatedCityGmlSource = new ValidatedLocalDatasetLocation(localSource.LocalSourcePath);
                break;
            case RemoteDatasetLocation remoteSource:
                if (!remoteSource.ServerUri.IsAbsoluteUri)
                {
                    validationErrors.Add("The --citygml-source value must be an absolute URI.");
                    break;
                }

                if (!LooksLikeSupportedArchiveUri(remoteSource.ServerUri))
                {
                    validationErrors.Add("The --citygml-source value must point directly to a .zip or .7z CityGML archive over http or https.");
                    break;
                }

                validatedCityGmlSource = new ValidatedRemoteDatasetLocation(remoteSource.ServerUri);
                break;
        }

        if (normalizedRequest.DemTextureSource is not null)
        {
            switch (normalizedRequest.DemTextureSource)
            {
                case LocalDatasetLocation localSource:
                    if (!File.Exists(localSource.LocalSourcePath))
                    {
                        validationErrors.Add($"The GeoTIFF source path '{localSource.LocalSourcePath}' must point to an existing file.");
                        break;
                    }

                    if (!LooksLikeSupportedLocalTerrainTextureSourcePath(localSource.LocalSourcePath))
                    {
                        validationErrors.Add(
                            $"The GeoTIFF source path '{localSource.LocalSourcePath}' must be a .tif/.tiff file or a .zip/.7z archive.");
                        break;
                    }

                    validatedDemTextureSource = new ValidatedLocalDatasetLocation(localSource.LocalSourcePath);
                    break;
                case RemoteDatasetLocation remoteSource:
                    if (!remoteSource.ServerUri.IsAbsoluteUri)
                    {
                        validationErrors.Add("The --geotiff-source value must be an absolute URI.");
                        break;
                    }

                    if (!LooksLikeSupportedTerrainTextureUri(remoteSource.ServerUri))
                    {
                        validationErrors.Add("The --geotiff-source value must point directly to a .tif, .tiff, .zip, or .7z resource over http or https.");
                        break;
                    }

                    validatedDemTextureSource = new ValidatedRemoteDatasetLocation(remoteSource.ServerUri);
                    break;
            }
        }

        if (!double.IsFinite(normalizedRequest.TerrainGridMetersPerVertex)
            || normalizedRequest.TerrainGridMetersPerVertex <= 0)
        {
            validationErrors.Add("The terrain grid meters-per-vertex value must be a finite value greater than zero.");
        }

        if (normalizedRequest.TerrainGridMaxResolution < 2)
        {
            validationErrors.Add("The terrain grid max resolution value must be at least 2.");
        }

        if (validationErrors.Count > 0)
        {
            validatedRequest = null;
            errors = validationErrors;
            return false;
        }

        validatedRequest = new ValidatedPlateauImportRequest(
            normalizedRequest.Dataset,
            normalizedRequest.MeshCode,
            meshCodePattern!,
            validatedCityGmlSource!,
            validatedDemTextureSource,
            normalizedPackageNames,
            normalizedRequest.GlobalExcludeLodLevels,
            normalizedPackageExclusions,
            normalizedPackagePatterns,
            normalizedRequest.IncludeMarkingAlways,
            normalizedRequest.TerrainMeshMode,
            normalizedRequest.TerrainGridMetersPerVertex,
            normalizedRequest.TerrainGridMaxResolution,
            normalizedRequest.ExcludeGsiTerrainTiles);
        errors = Array.Empty<string>();
        return true;
    }

    public static ValidatedPlateauImportRequest NormalizeAndValidateOrThrow(PlateauImportRequest request)
    {
        if (TryNormalizeAndValidate(request, out ValidatedPlateauImportRequest? validatedRequest, out IReadOnlyList<string> errors))
        {
            return validatedRequest!;
        }

        throw new PlateauImportValidationException(errors);
    }

    internal static bool LooksLikeSupportedArchiveUri(Uri serverUri)
    {
        ArgumentNullException.ThrowIfNull(serverUri);

        return HasSupportedHttpScheme(serverUri)
            && HasSupportedExtension(serverUri.AbsolutePath, SupportedRemoteArchiveExtensions);
    }

    internal static bool LooksLikeSupportedTerrainTextureUri(Uri serverUri)
    {
        ArgumentNullException.ThrowIfNull(serverUri);

        return HasSupportedHttpScheme(serverUri)
            && HasSupportedExtension(serverUri.AbsolutePath, SupportedTerrainTextureExtensions);
    }

    private static bool LooksLikeSupportedLocalDatasetSourcePath(string sourcePath)
    {
        return Directory.Exists(sourcePath) || HasSupportedExtension(sourcePath, SupportedRemoteArchiveExtensions);
    }

    private static bool LooksLikeSupportedLocalTerrainTextureSourcePath(string sourcePath)
    {
        return File.Exists(sourcePath) && HasSupportedExtension(sourcePath, SupportedTerrainTextureExtensions);
    }

    private static bool HasSupportedHttpScheme(Uri serverUri)
    {
        if (!string.Equals(serverUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(serverUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool HasSupportedExtension(string path, IReadOnlyList<string> supportedExtensions)
    {
        string extension = Path.GetExtension(path);
        return supportedExtensions.Any(
            supportedExtension => string.Equals(extension, supportedExtension, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddDuplicatePackageMapKeyError(
        IEnumerable<string> packageKeys,
        string fieldName,
        List<string> errors)
    {
        string[] duplicateNormalizedPackageNames = packageKeys
            .Select(TryNormalizePackageNameInput)
            .Where(static packageName => packageName is not null)
            .Select(static packageName => packageName!)
            .GroupBy(static packageName => packageName, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .OrderBy(static packageName => packageName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (duplicateNormalizedPackageNames.Length == 0)
        {
            return;
        }

        errors.Add(
            $"The {fieldName} value contains duplicate package keys after normalization: {string.Join(", ", duplicateNormalizedPackageNames)}.");
    }

    private static string NormalizePackageNameInput(string? packageName)
    {
        return packageName?.Trim() ?? string.Empty;
    }

    private static string? TryNormalizePackageNameInput(string? packageName)
    {
        string trimmedPackageName = NormalizePackageNameInput(packageName);
        return PlateauPackageCatalog.TryNormalizePackageName(trimmedPackageName, out string normalizedPackageName)
            ? normalizedPackageName
            : null;
    }

    private static PlateauImportRequest NormalizeRawRequest(PlateauImportRequest request)
    {
        return request with
        {
            Dataset = TrimToEmpty(request.Dataset),
            MeshCode = TrimToEmpty(request.MeshCode),
            CityGmlSource = NormalizeDatasetLocation(request.CityGmlSource),
            DemTextureSource = request.DemTextureSource is null ? null : NormalizeDatasetLocation(request.DemTextureSource),
            PackageNames = request.PackageNames is null
                ? null
                : request.PackageNames.Select(static packageName => TrimToEmpty(packageName)).ToArray(),
        };
    }

    private static DatasetLocation NormalizeDatasetLocation(DatasetLocation source)
    {
        return source switch
        {
            LocalDatasetLocation localSource => new LocalDatasetLocation(localSource.LocalSourcePath.Trim()),
            RemoteDatasetLocation remoteSource => remoteSource,
            _ => source,
        };
    }

    private static Dictionary<string, IReadOnlySet<int>> NormalizePackageExclusionMap(
        IReadOnlyDictionary<string, IReadOnlySet<int>> exclusionsByPackage)
    {
        Dictionary<string, IReadOnlySet<int>> normalized = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string packageName, IReadOnlySet<int> excludedLods) in exclusionsByPackage)
        {
            PlateauPackageCatalog.TryNormalizePackageName(packageName, out string normalizedPackageName);
            normalized[normalizedPackageName] = excludedLods;
        }

        return normalized;
    }

    private static Dictionary<string, string> NormalizePackagePatternMap(
        IReadOnlyDictionary<string, string> patternsByPackage)
    {
        Dictionary<string, string> normalized = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string packageName, string pattern) in patternsByPackage)
        {
            PlateauPackageCatalog.TryNormalizePackageName(packageName, out string normalizedPackageName);
            normalized[normalizedPackageName] = pattern;
        }

        return normalized;
    }

    private static string TrimToEmpty(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static bool LooksLikeSupportedLocalCityGmlSourcePath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath))
        {
            return true;
        }

        string extension = Path.GetExtension(fullPath);
        return SupportedRemoteArchiveExtensions.Any(
            supportedExtension => string.Equals(extension, supportedExtension, StringComparison.OrdinalIgnoreCase));
    }
}
