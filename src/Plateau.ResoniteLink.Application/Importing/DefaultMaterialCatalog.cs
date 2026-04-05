using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal static class DefaultMaterialCatalog
{
    public static ResolvedMaterial ResolveMaterial(
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
                TextureScale: null);
        }

        if (!string.IsNullOrWhiteSpace(texturePath))
        {
            return new ResolvedMaterial(
                ResoniteMaterialType.Standard,
                texturePath,
                ResoniteTextureSourceKind.Dataset,
                ResoniteMaterialProjection.Uv,
                Family: null,
                TextureScale: null);
        }

        bool useFacadeUvProjection = ShouldUseFacadeUvProjection(packageName, preferUvProjection);
        string family = familyOverride ?? ResolveBundledTextureFamily(packageName, useFacadeUvProjection);
        string selectedTexturePath = SelectBundledTexturePath(family, variantSelectionKey);
        return new ResolvedMaterial(
            ResoniteMaterialType.Standard,
            selectedTexturePath,
            ResoniteTextureSourceKind.Bundled,
            useFacadeUvProjection ? ResoniteMaterialProjection.Uv : ResoniteMaterialProjection.Triplanar,
            family,
            BundledDefaultMaterialProfiles.GetTilesPerMeter(selectedTexturePath));
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

        return BundledDefaultMaterialFamilies.Other;
    }

    private static string SelectBundledTexturePath(string family, string variantSelectionKey)
    {
        IReadOnlyList<string> variants = BundledDefaultMaterialFamilies.GetVariants(family);
        int hashCode = StringComparer.Ordinal.GetHashCode(variantSelectionKey) & int.MaxValue;
        int index = hashCode % variants.Count;
        return variants[index];
    }

    internal sealed record ResolvedMaterial(
        ResoniteMaterialType MaterialType,
        string? TexturePath,
        ResoniteTextureSourceKind TextureSourceKind,
        ResoniteMaterialProjection Projection,
        string? Family,
        ResoniteFloat2? TextureScale);
}
