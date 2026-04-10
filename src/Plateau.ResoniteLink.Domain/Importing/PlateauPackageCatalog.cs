using System.Collections.ObjectModel;

namespace Plateau.ResoniteLink.Domain.Importing;

public static class PlateauPackageCatalog
{
    private static readonly string[] BuildingPackageNamesStorage =
    [
        "bldg",
        "ubld",
    ];

    private static readonly string[] RoadPackageNamesStorage =
    [
        "tran",
        "rwy",
        "squr",
        "trk",
    ];

    private static readonly string[] PathLikePackageNamesStorage =
    [
        .. RoadPackageNamesStorage,
        "wwy",
    ];

    private static readonly string[] WireframeOverlayPackageNamesStorage =
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

    private static readonly string[] VegetationPackageNamesStorage =
    [
        "veg",
    ];

    private static readonly string[] CityFurniturePackageNamesStorage =
    [
        "frn",
    ];

    private static readonly string[] OtherMaterialPackageNamesStorage =
    [
        "brid",
        "cons",
        "gen",
        "tun",
        "unf",
        "wtr",
        "wwy",
    ];

    private static readonly string[] SupportedPackageNamesStorage =
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

    public static ReadOnlyCollection<string> BuildingPackageNames { get; } = Array.AsReadOnly(BuildingPackageNamesStorage);
    public static ReadOnlyCollection<string> RoadPackageNames { get; } = Array.AsReadOnly(RoadPackageNamesStorage);
    public static ReadOnlyCollection<string> PathLikePackageNames { get; } = Array.AsReadOnly(PathLikePackageNamesStorage);
    public static ReadOnlyCollection<string> WireframeOverlayPackageNames { get; } = Array.AsReadOnly(WireframeOverlayPackageNamesStorage);
    public static ReadOnlyCollection<string> VegetationPackageNames { get; } = Array.AsReadOnly(VegetationPackageNamesStorage);
    public static ReadOnlyCollection<string> CityFurniturePackageNames { get; } = Array.AsReadOnly(CityFurniturePackageNamesStorage);
    public static ReadOnlyCollection<string> OtherMaterialPackageNames { get; } = Array.AsReadOnly(OtherMaterialPackageNamesStorage);
    public static ReadOnlyCollection<string> SupportedPackageNames { get; } = Array.AsReadOnly(SupportedPackageNamesStorage);

    private static readonly Dictionary<string, string> PackageAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["waterbody"] = "wtr",
        };

    private static readonly HashSet<string> SupportedPackageNameSet =
        new(SupportedPackageNamesStorage, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> BuildingPackageNameSet =
        new(BuildingPackageNamesStorage, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> RoadPackageNameSet =
        new(RoadPackageNamesStorage, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> PathLikePackageNameSet =
        new(PathLikePackageNamesStorage, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> WireframeOverlayPackageNameSet =
        new(WireframeOverlayPackageNamesStorage, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> VegetationPackageNameSet =
        new(VegetationPackageNamesStorage, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> CityFurniturePackageNameSet =
        new(CityFurniturePackageNamesStorage, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> OtherMaterialPackageNameSet =
        new(OtherMaterialPackageNamesStorage, StringComparer.OrdinalIgnoreCase);

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

    public static IReadOnlyList<string> NormalizeRequestedPackageNames(IEnumerable<string> packageNames)
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

        return Array.AsReadOnly(normalizedPackageNames.ToArray());
    }

    public static bool IsBuildingPackage(string packageName)
    {
        return BuildingPackageNameSet.Contains(packageName);
    }

    public static bool IsRoadPackage(string packageName)
    {
        return RoadPackageNameSet.Contains(packageName);
    }

    public static bool IsPathLikePackage(string packageName)
    {
        return PathLikePackageNameSet.Contains(packageName);
    }

    public static bool IsWireframeOverlayPackage(string packageName)
    {
        return WireframeOverlayPackageNameSet.Contains(packageName);
    }

    public static bool IsVegetationPackage(string packageName)
    {
        return VegetationPackageNameSet.Contains(packageName);
    }

    public static bool IsCityFurniturePackage(string packageName)
    {
        return CityFurniturePackageNameSet.Contains(packageName);
    }

    public static bool IsOtherMaterialPackage(string packageName)
    {
        return OtherMaterialPackageNameSet.Contains(packageName);
    }
}
