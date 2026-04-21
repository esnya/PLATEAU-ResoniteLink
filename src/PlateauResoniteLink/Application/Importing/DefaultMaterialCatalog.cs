using System;
using System.Collections.Generic;

using System.Security.Cryptography;
using System.Text;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class DefaultMaterialResolver : IDefaultMaterialResolver
{
    public ResolvedMaterial ResolveMaterial(
        string packageName,
        ResoniteTexturePayload? texturePayload,
        bool preferUvProjection,
        string? familyOverride,
        string variantSelectionKey)
    {
        return ResolveMaterialCore(
            packageName,
            texturePayload,
            preferUvProjection,
            familyOverride,
            variantSelectionKey);
    }

    public static ResolvedMaterial ResolveMaterialCore(
        string packageName,
        ResoniteTexturePayload? texturePayload,
        bool preferUvProjection,
        string? familyOverride,
        string variantSelectionKey)
    {
        if (ShouldUseWireframeMaterial(packageName))
        {
            return new ResolvedMaterial(
                ResoniteMaterialType.Wireframe,
                TexturePayload: null,
                ResoniteTextureSourceKind.Bundled,
                ResoniteMaterialProjection.Uv,
                Family: null,
                TextureScale: null,
                AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped);
        }

        if (texturePayload is not null)
        {
            return new ResolvedMaterial(
                ResoniteMaterialType.Standard,
                texturePayload,
                ResoniteTextureSourceKind.Dataset,
                ResoniteMaterialProjection.Uv,
                Family: null,
                TextureScale: null,
                AssetScope: ResoniteMaterialAssetScope.PresentationSlotScoped);
        }

        bool useFacadeUvProjection = ShouldUseFacadeUvProjection(packageName, preferUvProjection);
        string family = familyOverride ?? ResolveBundledTextureFamily(packageName, useFacadeUvProjection);
        int bundledVariantIndex = SelectBundledVariantIndex(family, variantSelectionKey);
        return new ResolvedMaterial(
            ResoniteMaterialType.Standard,
            TexturePayload: null,
            ResoniteTextureSourceKind.Bundled,
            preferUvProjection ? ResoniteMaterialProjection.Uv : ResoniteMaterialProjection.Triplanar,
            family,
            BundledDefaultMaterialProfiles.GetTilesPerMeter(BundledDefaultMaterialFamilies.GetVariant(family, bundledVariantIndex)),
            ResoniteMaterialAssetScope.Common,
            BundledVariantIndex: bundledVariantIndex);
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

        if (PlateauPackageCatalog.IsRoadPackage(packageName)
            || PlateauPackageCatalog.IsPathLikePackage(packageName))
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

    private static int SelectBundledVariantIndex(string family, string variantSelectionKey)
    {
        IReadOnlyList<string> variants = BundledDefaultMaterialFamilies.GetVariants(family);
        byte[] keyBytes = Encoding.UTF8.GetBytes(variantSelectionKey);
        byte[] hashBytes = SHA256.HashData(keyBytes);
        int hashCode = BitConverter.ToInt32(hashBytes, 0) & int.MaxValue;
        return hashCode % variants.Count;
    }
}
