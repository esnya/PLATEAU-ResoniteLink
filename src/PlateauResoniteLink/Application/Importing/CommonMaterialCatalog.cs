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

    public CommonMaterialCatalogSnapshot Create()
    {
        SortedSet<string> families = new(StringComparer.Ordinal);
        AddBuildingFamilies(families);
        families.Add(BundledDefaultMaterialFamilies.RoadUv);
        families.Add(BundledDefaultMaterialFamilies.RoadTriplanar);
        families.Add(BundledDefaultMaterialFamilies.Vegetation);
        families.Add(BundledDefaultMaterialFamilies.CityFurniture);
        families.Add(BundledDefaultMaterialFamilies.Other);

        List<MaterialBinding> materials = [];
        foreach (string family in families)
        {
            int variantCount = BundledDefaultMaterialFamilies.GetVariantDefinitions(family).Count;
            for (int variantIndex = 0; variantIndex < variantCount; variantIndex++)
            {
                materials.Add(CreateBinding(family, variantIndex));
            }
        }

        materials.AddRange(CreateSharedAlbedoCommonMaterialBindings());
        materials.AddRange(CreateSharedVertexColorCommonMaterialBindings());

        return new CommonMaterialCatalogSnapshot(materials);
    }

    private static void AddBuildingFamilies(SortedSet<string> families)
    {
        families.Add(BundledDefaultMaterialFamilies.Roof);
        foreach (string family in BundledDefaultMaterialFamilies.BuildingFacadeFamilies)
        {
            families.Add(family);
        }
    }

    private static MaterialBinding CreateBinding(
        string family,
        int variantIndex)
    {
        string texturePath = BundledDefaultMaterialFamilies.GetVariant(family, variantIndex);
        BundledDefaultMaterialProfile uvProfile = BundledDefaultMaterialProfiles.GetProfile(texturePath);
        Float2 textureScale = ToContract(uvProfile.TextureScale);
        Float2? textureOffset = uvProfile.TextureOffset is null ? null : ToContract(uvProfile.TextureOffset);
        MaterialProjection projection = GetDefaultProjection(family);
        return new MaterialBinding(
            MaterialKey: CreateMaterialKey(family, variantIndex),
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
        int variantIndex)
    {
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{family}/{BundledDefaultMaterialFamilies.GetVariantMaterialName(family, variantIndex)}");
    }

    private static MaterialProjection GetDefaultProjection(string family)
    {
        if (string.Equals(family, BundledDefaultMaterialFamilies.RoadUv, StringComparison.Ordinal)
            || BundledDefaultMaterialFamilies.BuildingFacadeFamilies.Contains(family, StringComparer.Ordinal))
        {
            return MaterialProjection.Uv;
        }

        return MaterialProjection.Triplanar;
    }

    private static IReadOnlyList<MaterialBinding> CreateSharedAlbedoCommonMaterialBindings()
    {
        return
        [
            CreateSharedAlbedoCommonMaterialBinding(depthOffset: null),
            CreateSharedAlbedoCommonMaterialBinding(LocalCityGmlObjectProjection.DefaultTerrainAlignedMaterialDepthOffset),
        ];
    }

    private static MaterialBinding CreateSharedAlbedoCommonMaterialBinding(MaterialDepthOffset? depthOffset)
    {
        return new MaterialBinding(
            MaterialKey: CreateCanonicalGenericSharedMaterialKey(
                MaterialProjection.Uv,
                textureScale: null,
                textureOffset: null,
                depthOffset),
            BaseColor: CanonicalBaseColor,
            MaterialType: MaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: TextureSourceKind.Dataset,
            Projection: MaterialProjection.Uv,
            DepthOffset: depthOffset,
            SubmeshIndices: [0],
            TextureScale: null,
            Family: null,
            TextureOffset: null,
            ReuseScope: MaterialReuseScope.Shared);
    }

    private static IReadOnlyList<MaterialBinding> CreateSharedVertexColorCommonMaterialBindings()
    {
        return
        [
            CreateSharedVertexColorCommonMaterialBinding(depthOffset: null),
            CreateSharedVertexColorCommonMaterialBinding(LocalCityGmlObjectProjection.DefaultTerrainAlignedMaterialDepthOffset),
        ];
    }

    private static MaterialBinding CreateSharedVertexColorCommonMaterialBinding(MaterialDepthOffset? depthOffset)
    {
        return new MaterialBinding(
            MaterialKey: CreateCanonicalVertexColorCommonMaterialKey(
                MaterialProjection.Uv,
                depthOffset),
            BaseColor: CanonicalBaseColor,
            MaterialType: MaterialType.VertexColor,
            TexturePayload: null,
            TextureSourceKind: TextureSourceKind.Bundled,
            Projection: MaterialProjection.Uv,
            DepthOffset: depthOffset,
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
        _ = textureScale;
        _ = textureOffset;

        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"generic/{ProjectionToken(projection)}{TerrainAlignedSuffix(depthOffset)}");
    }

    private static string CreateCanonicalVertexColorCommonMaterialKey(
        MaterialProjection projection,
        MaterialDepthOffset? depthOffset)
    {
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"vertex-color/{ProjectionToken(projection)}{TerrainAlignedSuffix(depthOffset)}");
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

    private static string TerrainAlignedSuffix(MaterialDepthOffset? value) => value is null ? string.Empty : "-terrain-aligned";

    private static Float2 ToContract(Domain.Importing.ScalarPair value) => new(value.X, value.Y);
}
