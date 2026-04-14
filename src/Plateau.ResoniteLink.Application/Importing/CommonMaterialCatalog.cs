using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal static class CommonMaterialCatalog
{
    private static readonly ResoniteColor CanonicalBaseColor = new(1.0, 1.0, 1.0, 1.0);

    public static IReadOnlyList<ResoniteMaterialBinding> CreateForPackages(
        IReadOnlyList<string> packageNames)
    {
        ArgumentNullException.ThrowIfNull(packageNames);

        SortedSet<string> families = new(StringComparer.Ordinal);
        foreach (string packageName in packageNames.Where(static name => !string.IsNullOrWhiteSpace(name)))
        {
            if (PlateauPackageCatalog.IsBuildingPackage(packageName))
            {
                families.Add(BundledDefaultMaterialFamilies.Facade);
                families.Add(BundledDefaultMaterialFamilies.Roof);
                continue;
            }

            if (PlateauPackageCatalog.IsRoadPackage(packageName) || PlateauPackageCatalog.IsPathLikePackage(packageName))
            {
                families.Add(BundledDefaultMaterialFamilies.Road);
                continue;
            }

            if (PlateauPackageCatalog.IsVegetationPackage(packageName))
            {
                families.Add(BundledDefaultMaterialFamilies.Vegetation);
                continue;
            }

            if (PlateauPackageCatalog.IsCityFurniturePackage(packageName))
            {
                families.Add(BundledDefaultMaterialFamilies.CityFurniture);
                continue;
            }

            if (PlateauPackageCatalog.IsOtherMaterialPackage(packageName))
            {
                families.Add(BundledDefaultMaterialFamilies.Other);
            }
        }

        List<ResoniteMaterialBinding> materials = [];
        foreach (string family in families)
        {
            foreach (string texturePath in BundledDefaultMaterialFamilies.GetVariants(family).OrderBy(static path => path, StringComparer.Ordinal))
            {
                materials.Add(CreateBinding(family, texturePath, ResoniteMaterialProjection.Uv));
                materials.Add(CreateBinding(family, texturePath, ResoniteMaterialProjection.Triplanar));
            }
        }

        return materials;
    }

    private static ResoniteMaterialBinding CreateBinding(
        string family,
        string texturePath,
        ResoniteMaterialProjection projection)
    {
        return new ResoniteMaterialBinding(
            MaterialKey: CreateMaterialKey(family, texturePath, projection),
            BaseColor: CanonicalBaseColor,
            MaterialType: ResoniteMaterialType.Standard,
            TexturePath: texturePath,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: projection,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: BundledDefaultMaterialProfiles.GetTilesPerMeter(texturePath),
            Family: family,
            TextureOffset: null,
            AssetScope: ResoniteMaterialAssetScope.Common);
    }

    private static string CreateMaterialKey(
        string family,
        string texturePath,
        ResoniteMaterialProjection projection)
    {
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"common|{family}|{projection}|{texturePath}");
    }
}
