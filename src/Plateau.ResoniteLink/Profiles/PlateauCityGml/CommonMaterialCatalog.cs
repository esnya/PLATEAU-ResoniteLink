using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public static class CommonMaterialCatalog
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
            for (int variantIndex = 0; variantIndex < BundledDefaultMaterialFamilies.GetVariants(family).Count; variantIndex++)
            {
                materials.Add(CreateBinding(family, variantIndex, ResoniteMaterialProjection.Uv));
                materials.Add(CreateBinding(family, variantIndex, ResoniteMaterialProjection.Triplanar));
            }
        }

        materials.Add(ResoniteMaterialSharing.CreateSharedAlbedoCommonMaterial());
        materials.Add(ResoniteMaterialSharing.CreateSharedVertexColorCommonMaterial());
        foreach (ResoniteFloat2 offset in ResoniteMaterialSharing.FixedSharedAlbedoOffsets)
        {
            materials.Add(ResoniteMaterialSharing.CreateSharedAlbedoCommonMaterial(offset));
        }

        return materials;
    }

    private static ResoniteMaterialBinding CreateBinding(
        string family,
        int variantIndex,
        ResoniteMaterialProjection projection)
    {
        string texturePath = BundledDefaultMaterialFamilies.GetVariant(family, variantIndex);
        return new ResoniteMaterialBinding(
            MaterialKey: CreateMaterialKey(family, variantIndex, projection),
            BaseColor: CanonicalBaseColor,
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: projection,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: BundledDefaultMaterialProfiles.GetTilesPerMeter(texturePath),
            Family: family,
            TextureOffset: null,
            AssetScope: ResoniteMaterialAssetScope.Common,
            BundledVariantIndex: variantIndex);
    }

    private static string CreateMaterialKey(
        string family,
        int variantIndex,
        ResoniteMaterialProjection projection)
    {
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"common|{family}|variant:{variantIndex}|{projection}");
    }
}
