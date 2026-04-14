using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal sealed record TerrainContext(
    TerrainHeightSampler? TerrainHeightSampler,
    int ParsedDemCityObjectCount,
    int TerrainTriangleCount)
{
    public static TerrainContext Empty { get; } = new(null, 0, 0);
}

internal static class LocalCityGmlTerrainDependency
{
    public static bool IsTerrainDependent(BootstrapParsedCityObject cityObject)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        return string.Equals(cityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
            || ShouldTerrainAlignCityObject(cityObject);
    }

    private static bool ShouldTerrainAlignCityObject(BootstrapParsedCityObject cityObject)
    {
        string packageName = cityObject.PackageName.ToLowerInvariant();
        if (PlateauPackageCatalog.IsRoadPackage(packageName))
        {
            return !cityObject.LodLevel.HasValue || cityObject.LodLevel.Value < 3;
        }

        return packageName switch
        {
            "fld" or "ifld" or "lsld" or "luse" or "rfld" or "tnm" or "urf" or "wtr" or "wwy" => true,
            _ => false,
        };
    }
}
