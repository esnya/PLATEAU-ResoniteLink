using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

public static class CommonMaterialCatalog
{
    private static readonly ColorRgba CanonicalBaseColor = new(1.0, 1.0, 1.0, 1.0);

    public static IReadOnlyList<MaterialBinding> CreateForPackages(
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

        List<MaterialBinding> materials = [];
        foreach (string family in families)
        {
            for (int variantIndex = 0; variantIndex < BundledDefaultMaterialFamilies.GetVariants(family).Count; variantIndex++)
            {
                materials.Add(CreateBinding(family, variantIndex, MaterialProjection.Uv));
                materials.Add(CreateBinding(family, variantIndex, MaterialProjection.Triplanar));
            }
        }

        materials.Add(SceneImportContractMapper.ToContract(ResoniteMaterialSharing.CreateSharedAlbedoCommonMaterial()));
        materials.Add(SceneImportContractMapper.ToContract(ResoniteMaterialSharing.CreateSharedVertexColorCommonMaterial()));

        return materials;
    }

    private static MaterialBinding CreateBinding(
        string family,
        int variantIndex,
        MaterialProjection projection)
    {
        string texturePath = BundledDefaultMaterialFamilies.GetVariant(family, variantIndex);
        return new MaterialBinding(
            MaterialKey: CreateMaterialKey(family, variantIndex, projection),
            BaseColor: CanonicalBaseColor,
            MaterialType: MaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: TextureSourceKind.Bundled,
            Projection: projection,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: ToContract(BundledDefaultMaterialProfiles.GetTilesPerMeter(texturePath)),
            Family: family,
            TextureOffset: null,
            ReuseScope: MaterialReuseScope.Shared,
            BundledVariantIndex: variantIndex);
    }

    private static string CreateMaterialKey(
        string family,
        int variantIndex,
        MaterialProjection projection)
    {
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"common|{family}|variant:{variantIndex}|{projection}");
    }

    private static Float2 ToContract(ResoniteFloat2 value) => new(value.X, value.Y);
}
