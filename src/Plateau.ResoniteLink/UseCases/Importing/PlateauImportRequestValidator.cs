using System.Text.RegularExpressions;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public static class PlateauImportRequestValidator
{
    private static readonly string[] SupportedRemoteArchiveExtensions = [".zip", ".7z"];

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
        ValidatedPlateauImportSource? validatedSource = null;

        if (string.IsNullOrWhiteSpace(normalizedRequest.Dataset))
        {
            validationErrors.Add("The dataset value is required.");
        }

        if (string.IsNullOrWhiteSpace(normalizedRequest.MeshCode))
        {
            validationErrors.Add("The mesh code value is required.");
        }
        else if (!MeshCodeInput.TryCreateRegex(normalizedRequest.MeshCode, out meshCodePattern, out string? meshCodeError))
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

        switch (normalizedRequest.Source)
        {
            case PlateauLocalImportSource localSource:
                if (string.IsNullOrWhiteSpace(localSource.LocalSourcePath))
                {
                    validationErrors.Add("The --local-source-path value is required when --source local is used.");
                    break;
                }

                if (!Directory.Exists(localSource.LocalSourcePath)
                    && !File.Exists(localSource.LocalSourcePath))
                {
                    validationErrors.Add($"The local source path '{localSource.LocalSourcePath}' does not exist.");
                    break;
                }

                validatedSource = new ValidatedPlateauLocalImportSource(localSource.LocalSourcePath);
                break;
            case PlateauRemoteImportSource remoteSource:
                if (remoteSource.ServerUri is null)
                {
                    validationErrors.Add("The --server-url value is required when --source remote is used.");
                    break;
                }

                if (!remoteSource.ServerUri.IsAbsoluteUri)
                {
                    validationErrors.Add("The --server-url value must be an absolute URI.");
                    break;
                }

                if (!LooksLikeSupportedArchiveUri(remoteSource.ServerUri))
                {
                    validationErrors.Add("The --server-url value must point directly to a .zip or .7z CityGML archive over http or https.");
                    break;
                }

                validatedSource = new ValidatedPlateauRemoteImportSource(remoteSource.ServerUri);
                break;
        }

        if (normalizedRequest.DemHeightmapMetersPerVertex <= 0)
        {
            validationErrors.Add("The DEM heightmap meters-per-vertex value must be greater than zero.");
        }

        if (normalizedRequest.DemHeightmapMaxResolution < 2)
        {
            validationErrors.Add("The DEM heightmap max resolution value must be at least 2.");
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
            validatedSource!,
            normalizedPackageNames,
            normalizedRequest.GlobalExcludeLodLevels,
            normalizedPackageExclusions,
            normalizedPackagePatterns,
            normalizedRequest.IncludeMarkingAlways,
            normalizedRequest.DemTerrainMode,
            normalizedRequest.DemHeightmapMetersPerVertex,
            normalizedRequest.DemHeightmapMaxResolution);
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

        if (!string.Equals(serverUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(serverUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string extension = Path.GetExtension(serverUri.AbsolutePath);
        return SupportedRemoteArchiveExtensions.Any(
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
            Source = NormalizeSource(request.Source),
            PackageNames = request.PackageNames is null
                ? null
                : request.PackageNames.Select(static packageName => TrimToEmpty(packageName)).ToArray(),
        };
    }

    private static PlateauImportSource NormalizeSource(PlateauImportSource source)
    {
        return source switch
        {
            PlateauLocalImportSource localSource => new PlateauLocalImportSource(
                string.IsNullOrWhiteSpace(localSource.LocalSourcePath)
                    ? null
                    : localSource.LocalSourcePath.Trim()),
            PlateauRemoteImportSource remoteSource => remoteSource.ServerUri is null
                ? new PlateauRemoteImportSource(null)
                : remoteSource,
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
}
