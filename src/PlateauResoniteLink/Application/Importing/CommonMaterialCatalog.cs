using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1822:Mark members as static",
    Justification = "The catalog intentionally stays instance-based so material selection can remain a replaceable service seam.")]
public sealed class CommonMaterialCatalog
{
    private static readonly ColorRgba CanonicalBaseColor = new(1.0, 1.0, 1.0, 1.0);

    public IReadOnlyList<MaterialBinding> CreateForPackages(
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

        materials.Add(CreateSharedAlbedoCommonMaterialBinding());
        materials.Add(CreateSharedVertexColorCommonMaterialBinding());

        return materials;
    }

    private static MaterialBinding CreateBinding(
        string family,
        int variantIndex,
        MaterialProjection projection)
    {
        string texturePath = BundledDefaultMaterialFamilies.GetVariant(family, variantIndex);
        BundledDefaultMaterialProfile uvProfile = BundledDefaultMaterialProfiles.GetProfile(texturePath);
        return new MaterialBinding(
            MaterialKey: CreateMaterialKey(family, variantIndex, projection),
            BaseColor: CanonicalBaseColor,
            MaterialType: MaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: TextureSourceKind.Bundled,
            Projection: projection,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: ToContract(uvProfile.TextureScale),
            Family: family,
            TextureOffset: uvProfile.TextureOffset is null ? null : ToContract(uvProfile.TextureOffset),
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

    private static MaterialBinding CreateSharedAlbedoCommonMaterialBinding()
    {
        return new MaterialBinding(
            MaterialKey: CreateCanonicalGenericSharedMaterialKey(
                MaterialProjection.Uv,
                textureScale: null,
                textureOffset: null,
                depthOffset: null),
            BaseColor: CanonicalBaseColor,
            MaterialType: MaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: TextureSourceKind.Dataset,
            Projection: MaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: null,
            Family: null,
            TextureOffset: null,
            ReuseScope: MaterialReuseScope.Shared);
    }

    private static MaterialBinding CreateSharedVertexColorCommonMaterialBinding()
    {
        return new MaterialBinding(
            MaterialKey: CreateCanonicalVertexColorCommonMaterialKey(
                MaterialProjection.Uv,
                depthOffset: null),
            BaseColor: CanonicalBaseColor,
            MaterialType: MaterialType.VertexColor,
            TexturePayload: null,
            TextureSourceKind: TextureSourceKind.Bundled,
            Projection: MaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: null,
            Family: null,
            TextureOffset: null,
            ReuseScope: MaterialReuseScope.Shared);
    }

    private static string CreateCanonicalGenericSharedMaterialKey(
        MaterialProjection projection,
        Float2? textureScale,
        Float2? textureOffset,
        MaterialDepthOffset? depthOffset)
    {
        string scaleToken = CreateFloat2Token(textureScale);
        string offsetToken = CreateFloat2Token(textureOffset);
        string depthToken = depthOffset is null
            ? "none"
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{depthOffset.Factor:0.######}x{depthOffset.Units:0.######}");
        return $"generic|{projection}|scale:{scaleToken}|offset:{offsetToken}|depth:{depthToken}";
    }

    private static string CreateCanonicalVertexColorCommonMaterialKey(
        MaterialProjection projection,
        MaterialDepthOffset? depthOffset)
    {
        return $"vertex-color|{projection}|depth:{(depthOffset is null ? "none" : string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{depthOffset.Factor:0.######}x{depthOffset.Units:0.######}"))}";
    }

    private static Float2 ToContract(Domain.Importing.ScalarPair value) => new(value.X, value.Y);

    private static string CreateFloat2Token(Float2? value)
    {
        return value is null
            ? "none"
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{value.X:0.######}x{value.Y:0.######}");
    }
}
