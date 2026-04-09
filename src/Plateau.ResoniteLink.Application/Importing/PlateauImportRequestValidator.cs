using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public static class PlateauImportRequestValidator
{
    private static readonly string[] SupportedRemoteArchiveExtensions = [".zip", ".7z"];

    public static IReadOnlyList<string> Validate(PlateauImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<string> errors = [];

        if (string.IsNullOrWhiteSpace(request.Dataset))
        {
            errors.Add("The dataset value is required.");
        }

        if (string.IsNullOrWhiteSpace(request.MeshCode))
        {
            errors.Add("The mesh code value is required.");
        }

        if (request.PackageNames is not null)
        {
            if (request.PackageNames.Count == 0)
            {
                errors.Add("At least one package name is required when packages are specified.");
            }
            else
            {
                string[] unsupportedPackageNames = request.PackageNames
                    .Select(static packageName => NormalizePackageNameInput(packageName))
                    .Where(static packageName => !PlateauPackageCatalog.TryNormalizePackageName(packageName, out _))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(packageName => packageName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (unsupportedPackageNames.Length > 0)
                {
                    errors.Add(
                        $"Unsupported package name(s): {string.Join(", ", unsupportedPackageNames)}. Supported packages: {string.Join(", ", PlateauPackageCatalog.SupportedPackageNames)}.");
                }
            }
        }

        if (request.ExcludeLodLevelsByPackage is not null)
        {
            AddDuplicatePackageMapKeyError(
                request.ExcludeLodLevelsByPackage.Keys,
                "ExcludeLodLevelsByPackage",
                errors);

            string[] unsupportedPackageNames = request.ExcludeLodLevelsByPackage.Keys
                .Select(static packageName => NormalizePackageNameInput(packageName))
                .Where(static packageName => !PlateauPackageCatalog.TryNormalizePackageName(packageName, out _))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(packageName => packageName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (unsupportedPackageNames.Length > 0)
            {
                errors.Add(
                    $"Unsupported package name(s): {string.Join(", ", unsupportedPackageNames)}. Supported packages: {string.Join(", ", PlateauPackageCatalog.SupportedPackageNames)}.");
            }
        }

        if (request.PackagePatterns is not null)
        {
            AddDuplicatePackageMapKeyError(
                request.PackagePatterns.Keys,
                "PackagePatterns",
                errors);

            string[] unsupportedPackageNames = request.PackagePatterns.Keys
                .Select(static packageName => NormalizePackageNameInput(packageName))
                .Where(static packageName => !PlateauPackageCatalog.TryNormalizePackageName(packageName, out _))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(packageName => packageName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (unsupportedPackageNames.Length > 0)
            {
                errors.Add(
                    $"Unsupported package name(s): {string.Join(", ", unsupportedPackageNames)}. Supported packages: {string.Join(", ", PlateauPackageCatalog.SupportedPackageNames)}.");
            }
        }

        switch (request.SourceKind)
        {
            case DatasetSourceKind.Local:
                if (string.IsNullOrWhiteSpace(request.LocalSourcePath))
                {
                    errors.Add("The --local-source-path value is required when --source local is used.");
                    break;
                }

                if (!Directory.Exists(request.LocalSourcePath)
                    && !File.Exists(request.LocalSourcePath))
                {
                    errors.Add($"The local source path '{request.LocalSourcePath}' does not exist.");
                }

                break;
            case DatasetSourceKind.Remote:
                if (request.ServerUri is null)
                {
                    errors.Add("The --server-url value is required when --source remote is used.");
                    break;
                }

                if (!request.ServerUri.IsAbsoluteUri)
                {
                    errors.Add("The --server-url value must be an absolute URI.");
                    break;
                }

                if (!LooksLikeSupportedArchiveUri(request.ServerUri))
                {
                    errors.Add("The --server-url value must point directly to a .zip or .7z CityGML archive over http or https.");
                }

                break;
        }

        if (request.DemHeightmapMetersPerVertex <= 0)
        {
            errors.Add("The DEM heightmap meters-per-vertex value must be greater than zero.");
        }

        if (request.DemHeightmapMaxResolution < 2)
        {
            errors.Add("The DEM heightmap max resolution value must be at least 2.");
        }

        return errors;
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
}
