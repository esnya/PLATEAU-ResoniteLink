using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal sealed class DefaultMaterialResolver : IDefaultMaterialResolver
{
    public ResolvedMaterial ResolveMaterial(
        string packageName,
        string? texturePath,
        bool preferUvProjection,
        string? familyOverride,
        string variantSelectionKey)
    {
        if (ShouldUseWireframeMaterial(packageName))
        {
            return new ResolvedMaterial(
                ResoniteMaterialType.Wireframe,
                TexturePath: null,
                ResoniteTextureSourceKind.Bundled,
                ResoniteMaterialProjection.Uv,
                Family: null,
                TextureScale: null,
                AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped);
        }

        if (!string.IsNullOrWhiteSpace(texturePath))
        {
            return new ResolvedMaterial(
                ResoniteMaterialType.Standard,
                texturePath,
                ResoniteTextureSourceKind.Dataset,
                ResoniteMaterialProjection.Uv,
                Family: null,
                TextureScale: null,
                AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped);
        }

        bool useFacadeUvProjection = ShouldUseFacadeUvProjection(packageName, preferUvProjection);
        string family = familyOverride ?? ResolveBundledTextureFamily(packageName, useFacadeUvProjection);
        string selectedTexturePath = SelectBundledTexturePath(family, variantSelectionKey);
        return new ResolvedMaterial(
            ResoniteMaterialType.Standard,
            selectedTexturePath,
            ResoniteTextureSourceKind.Bundled,
            preferUvProjection ? ResoniteMaterialProjection.Uv : ResoniteMaterialProjection.Triplanar,
            family,
            BundledDefaultMaterialProfiles.GetTilesPerMeter(selectedTexturePath),
            ResoniteMaterialAssetScope.Common);
    }

    private static bool ShouldUseWireframeMaterial(string packageName)
    {
        return PlateauPackageCatalog.IsWireframeOverlayPackage(packageName);
    }

    private static bool ShouldUseFacadeUvProjection(string packageName, bool preferUvProjection)
    {
        return preferUvProjection
            && PlateauPackageCatalog.IsBuildingPackage(packageName);
    }

    private static string ResolveBundledTextureFamily(string packageName, bool useFacadeUvProjection)
    {
        if (useFacadeUvProjection)
        {
            return BundledDefaultMaterialFamilies.Facade;
        }

        if (PlateauPackageCatalog.IsBuildingPackage(packageName))
        {
            return BundledDefaultMaterialFamilies.Roof;
        }

        if (PlateauPackageCatalog.IsRoadPackage(packageName))
        {
            return BundledDefaultMaterialFamilies.Road;
        }

        if (PlateauPackageCatalog.IsVegetationPackage(packageName))
        {
            return BundledDefaultMaterialFamilies.Vegetation;
        }

        if (PlateauPackageCatalog.IsCityFurniturePackage(packageName))
        {
            return BundledDefaultMaterialFamilies.CityFurniture;
        }

        return BundledDefaultMaterialFamilies.Other;
    }

    private static string SelectBundledTexturePath(string family, string variantSelectionKey)
    {
        IReadOnlyList<string> variants = BundledDefaultMaterialFamilies.GetVariants(family);
        int hashCode = StringComparer.Ordinal.GetHashCode(variantSelectionKey) & int.MaxValue;
        int index = hashCode % variants.Count;
        return variants[index];
    }
}
