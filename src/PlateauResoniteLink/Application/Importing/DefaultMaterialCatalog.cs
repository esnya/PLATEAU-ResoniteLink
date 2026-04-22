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

    internal static ResolvedMaterial ResolveMaterialCore(
        string packageName,
        ResoniteTexturePayload? texturePayload,
        bool preferUvProjection,
        string? familyOverride,
        string variantSelectionKey)
    {
        if (ShouldUseWireframeMaterial(packageName))
        {
            return new ResolvedMaterial(
                MaterialType.Wireframe,
                TexturePayload: null,
                TextureSourceKind.Bundled,
                MaterialProjection.Uv,
                Family: null,
                TextureScale: null,
                ReuseScope: MaterialReuseScope.PerObject);
        }

        if (texturePayload is not null)
        {
            return new ResolvedMaterial(
                MaterialType.Standard,
                texturePayload,
                TextureSourceKind.Dataset,
                MaterialProjection.Uv,
                Family: null,
                TextureScale: null,
                ReuseScope: MaterialReuseScope.PerObject);
        }

        bool useFacadeUvProjection = ShouldUseFacadeUvProjection(packageName, preferUvProjection);
        string family = familyOverride ?? ResolveBundledTextureFamily(packageName, useFacadeUvProjection);
        int bundledVariantIndex = SelectBundledVariantIndex(family, variantSelectionKey);
        return new ResolvedMaterial(
            MaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind.Bundled,
            preferUvProjection ? MaterialProjection.Uv : MaterialProjection.Triplanar,
            family,
            BundledDefaultMaterialProfiles.GetTilesPerMeter(BundledDefaultMaterialFamilies.GetVariant(family, bundledVariantIndex)),
            MaterialReuseScope.Shared,
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
