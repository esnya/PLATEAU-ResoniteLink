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
        Float2 textureScale = ToContract(uvProfile.TextureScale);
        Float2? textureOffset = uvProfile.TextureOffset is null ? null : ToContract(uvProfile.TextureOffset);
        return new MaterialBinding(
            MaterialKey: CreateMaterialKey(family, variantIndex, projection, textureScale, textureOffset),
            BaseColor: CanonicalBaseColor,
            MaterialType: MaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: TextureSourceKind.Bundled,
            Projection: projection,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: textureScale,
            Family: family,
            TextureOffset: textureOffset,
            ReuseScope: MaterialReuseScope.Shared,
            BundledVariantIndex: variantIndex);
    }

    private static string CreateMaterialKey(
        string family,
        int variantIndex,
        MaterialProjection projection,
        Float2 textureScale,
        Float2? textureOffset)
    {
        Float2? effectiveOffset = IsZeroTextureOffset(textureOffset) ? null : textureOffset;
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"common-{family}-{variantIndex}-{ProjectionToken(projection)}-scale-{FormatRounded(textureScale.X)}-{FormatRounded(textureScale.Y)}-offset-{FormatFloat2(effectiveOffset)}");
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
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"shared-generic-{ProjectionToken(projection)}-scale-{FormatFloat2(textureScale)}-offset-{FormatFloat2(textureOffset)}-depth-{FormatDepth(depthOffset)}");
    }

    private static string CreateCanonicalVertexColorCommonMaterialKey(
        MaterialProjection projection,
        MaterialDepthOffset? depthOffset)
    {
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"shared-vertex-{ProjectionToken(projection)}-depth-{FormatDepth(depthOffset)}");
    }

    private static string ProjectionToken(MaterialProjection projection)
    {
        return projection switch
        {
            MaterialProjection.Uv => "uv",
            MaterialProjection.Triplanar => "triplanar",
            _ => projection.ToString().ToLowerInvariant(),
        };
    }

    private static Float2 ToContract(Domain.Importing.ScalarPair value) => new(value.X, value.Y);

    private static string FormatFloat2(Float2? value)
    {
        return value is null
            ? "none"
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{FormatRounded(value.X)}-{FormatRounded(value.Y)}");
    }

    private static string FormatDepth(MaterialDepthOffset? value)
    {
        return value is null
            ? "none"
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{FormatRounded(value.Factor)}-{FormatRounded(value.Units)}");
    }

    private static string FormatRounded(double value)
    {
        double rounded = Math.Round(value, 6, MidpointRounding.AwayFromZero);
        return (rounded == 0.0 ? 0.0 : rounded).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool IsZeroTextureOffset(Float2? textureOffset)
    {
        return textureOffset is null
            || (Math.Abs(textureOffset.X) < 1e-9
                && Math.Abs(textureOffset.Y) < 1e-9);
    }
}
