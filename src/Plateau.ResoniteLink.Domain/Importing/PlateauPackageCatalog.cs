namespace Plateau.ResoniteLink.Domain.Importing;

public static class PlateauPackageCatalog
{
    public static readonly string[] BuildingPackageNames =
    [
        "bldg",
        "ubld",
    ];

    public static readonly string[] RoadPackageNames =
    [
        "tran",
        "rwy",
        "squr",
        "trk",
    ];

    public static readonly string[] WireframeOverlayPackageNames =
    [
        "area",
        "fld",
        "htd",
        "ifld",
        "lsld",
        "luse",
        "rfld",
        "tnm",
        "urf",
    ];

    public static readonly string[] VegetationPackageNames =
    [
        "veg",
    ];

    public static readonly string[] OtherMaterialPackageNames =
    [
        "brid",
        "cons",
        "frn",
        "gen",
        "tun",
        "unf",
        "wtr",
        "wwy",
    ];

    public static readonly string[] SupportedPackageNames =
    [
        "area",
        "bldg",
        "brid",
        "cons",
        "dem",
        "fld",
        "frn",
        "gen",
        "htd",
        "ifld",
        "lsld",
        "luse",
        "rfld",
        "rwy",
        "squr",
        "tnm",
        "tran",
        "trk",
        "tun",
        "ubld",
        "unf",
        "urf",
        "veg",
        "wtr",
        "wwy",
    ];

    public static readonly string[] CliDefaultPackageNames =
    [
        "dem",
        "bldg",
        "brid",
        "frn",
        "tran",
        "rwy",
        "trk",
        "tun",
        "ubld",
        "unf",
        "veg",
    ];

    private static readonly Dictionary<string, string> PackageAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["waterbody"] = "wtr",
        };

    private static readonly HashSet<string> SupportedPackageNameSet =
        new(SupportedPackageNames, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> BuildingPackageNameSet =
        new(BuildingPackageNames, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> RoadPackageNameSet =
        new(RoadPackageNames, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> WireframeOverlayPackageNameSet =
        new(WireframeOverlayPackageNames, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> VegetationPackageNameSet =
        new(VegetationPackageNames, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> OtherMaterialPackageNameSet =
        new(OtherMaterialPackageNames, StringComparer.OrdinalIgnoreCase);

    public static bool TryNormalizePackageName(string value, out string normalizedPackageName)
    {
        normalizedPackageName = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmedValue = value.Trim();
        if (PackageAliases.TryGetValue(trimmedValue, out string? alias))
        {
            normalizedPackageName = alias;
            return true;
        }

        if (!SupportedPackageNameSet.Contains(trimmedValue))
        {
            return false;
        }

        normalizedPackageName = trimmedValue.ToLowerInvariant();
        return true;
    }

    public static string[] NormalizeRequestedPackageNames(IEnumerable<string> packageNames)
    {
        ArgumentNullException.ThrowIfNull(packageNames);

        List<string> normalizedPackageNames = [];
        HashSet<string> seenPackageNames = new(StringComparer.OrdinalIgnoreCase);

        foreach (string packageName in packageNames)
        {
            if (!TryNormalizePackageName(packageName, out string normalizedPackageName))
            {
                throw new ArgumentException(
                    $"Unsupported package '{packageName}'. Supported packages: {string.Join(", ", SupportedPackageNames)}.");
            }

            if (seenPackageNames.Add(normalizedPackageName))
            {
                normalizedPackageNames.Add(normalizedPackageName);
            }
        }

        return normalizedPackageNames.ToArray();
    }

    public static bool IsBuildingPackage(string packageName)
    {
        return BuildingPackageNameSet.Contains(packageName);
    }

    public static bool IsRoadPackage(string packageName)
    {
        return RoadPackageNameSet.Contains(packageName);
    }

    public static bool IsWireframeOverlayPackage(string packageName)
    {
        return WireframeOverlayPackageNameSet.Contains(packageName);
    }

    public static bool IsVegetationPackage(string packageName)
    {
        return VegetationPackageNameSet.Contains(packageName);
    }

    public static bool IsOtherMaterialPackage(string packageName)
    {
        return OtherMaterialPackageNameSet.Contains(packageName);
    }
}
