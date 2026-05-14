using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class ResoniteSceneMaterialConventions
{
    private static readonly IReadOnlyList<string> EmptyLookupNames = [];

    internal enum TextureMemberRole
    {
        Albedo,
        Normal,
        Height,
        Metallic,
        Emission,
        TerrainMainTextureOverride,
    }

    internal readonly record struct TextureSamplingPolicy(
        string? PreferredProfile,
        string? WrapMode);

    public static string CreateMaterialSlotName(ResoniteMaterialBinding material, bool useCommonMaterialAssets)
    {
        ArgumentNullException.ThrowIfNull(material);
        ResoniteMaterialBinding normalizedMaterial = ResoniteDynamicMaterialUvNormalizer.NormalizeMaterialBinding(material);

        if (useCommonMaterialAssets)
        {
            return CreateCommonMaterialSlotName(normalizedMaterial);
        }

        string componentKind = normalizedMaterial.MaterialType switch
        {
            ResoniteMaterialType.Standard => normalizedMaterial.Projection switch
            {
                ResoniteMaterialProjection.Uv => "pbs-uv",
                ResoniteMaterialProjection.Triplanar => "pbs-triplanar",
                _ => "material",
            },
            ResoniteMaterialType.VertexColor => "vertex-color",
            ResoniteMaterialType.Wireframe => "wireframe",
            _ => "material",
        };

        string projectionName = normalizedMaterial.Projection switch
        {
            ResoniteMaterialProjection.Uv => "uv",
            ResoniteMaterialProjection.Triplanar => "triplanar",
            _ => normalizedMaterial.Projection.ToString().ToLowerInvariant(),
        };

        string sourceName = normalizedMaterial.TerrainOverlay is not null
            ? CreateTerrainOverlayToken(normalizedMaterial.TerrainOverlay)
            : normalizedMaterial.TexturePayload is not null
                ? "payload"
            : normalizedMaterial.AssetScope == ResoniteMaterialAssetScope.Common
                ? $"bundled-v{normalizedMaterial.BundledVariantIndex ?? 0}"
            : normalizedMaterial.MaterialType.ToString();

        string familyName = string.IsNullOrWhiteSpace(normalizedMaterial.Family)
            ? "none"
            : normalizedMaterial.Family!;
        string colorName = CreateCompactColorSuffix(normalizedMaterial.BaseColor);
        string depthName = normalizedMaterial.DepthOffset is not null
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{normalizedMaterial.DepthOffset.Factor:0.######}x{normalizedMaterial.DepthOffset.Units:0.######}")
            : "none";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{componentKind}_{projectionName}_{sourceName}_{familyName}_{depthName}_{colorName}");
    }

    public static IReadOnlyList<string> CreateCommonMaterialSlotLookupNames(ResoniteMaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (material.AssetScope != ResoniteMaterialAssetScope.Common)
        {
            return EmptyLookupNames;
        }

        return [CreateMaterialSlotName(material, useCommonMaterialAssets: true)];
    }

    public static ResoniteMaterialProjection GetBundledCommonMaterialProjection(string family)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);

        return string.Equals(family, BundledDefaultMaterialFamilies.RoadUv, StringComparison.Ordinal)
            || BundledDefaultMaterialFamilies.BuildingFacadeFamilies.Contains(family, StringComparer.Ordinal)
            ? ResoniteMaterialProjection.Uv
            : ResoniteMaterialProjection.Triplanar;
    }

    public static string GetCommonMaterialFamilySlotName(ResoniteMaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(material);
        return material.MaterialType == ResoniteMaterialType.VertexColor
            ? "vertex-color"
            : string.IsNullOrWhiteSpace(material.Family)
                ? "generic"
                : material.Family!;
    }

    public static ResoniteMaterialBinding NormalizeBatchGroupedMaterialBinding(ResoniteMaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(material);
        material = ResoniteDynamicMaterialUvNormalizer.NormalizeMaterialBinding(material);

        if (material.AssetScope == ResoniteMaterialAssetScope.Common
            && material.MaterialType == ResoniteMaterialType.Standard
            && material.TexturePayload is null
            && material.TerrainOverlay is null
            && material.TextureSourceKind == ResoniteTextureSourceKind.Bundled
            && !string.IsNullOrWhiteSpace(material.Family)
            && (!IsWhiteBaseColor(material.BaseColor)
                || HasNonDefaultBundledTextureTransform(material)
                || material.DepthOffset is not null))
        {
            return material with
            {
                AssetScope = ResoniteMaterialAssetScope.PresentationSlotScoped,
            };
        }

        if (material.TerrainOverlay is not null
            && material.AssetScope == ResoniteMaterialAssetScope.Common)
        {
            return material with
            {
                AssetScope = ResoniteMaterialAssetScope.PresentationSlotScoped,
            };
        }

        if (material.AssetScope == ResoniteMaterialAssetScope.PresentationSlotScoped
            && IsWhiteBundledFamilyMaterial(material)
            && !HasNonDefaultBundledTextureTransform(material))
        {
            return material;
        }

        if (material.MaterialType == ResoniteMaterialType.Standard
            && material.TexturePayload is null
            && material.AssetScope != ResoniteMaterialAssetScope.Common
            && string.IsNullOrWhiteSpace(material.Family))
        {
            return material;
        }

        return material;
    }

    public static TextureIdentity CreateTextureIdentity(TextureMemberRole role)
    {
        return new TextureIdentity(role switch
        {
            TextureMemberRole.Albedo => "albedo",
            TextureMemberRole.Normal => "normal",
            TextureMemberRole.Height => "height",
            TextureMemberRole.Metallic => "metallic",
            TextureMemberRole.Emission => "emission",
            _ => throw new InvalidOperationException($"Texture role '{role}' does not have a planned texture identity."),
        });
    }

    public static TextureSamplingPolicy GetTextureSamplingPolicy(TextureMemberRole role)
    {
        return role switch
        {
            TextureMemberRole.Normal
                or TextureMemberRole.Height
                or TextureMemberRole.Metallic => new TextureSamplingPolicy(
                    ResoniteTextureColorProfiles.Linear,
                    WrapMode: null),
            TextureMemberRole.TerrainMainTextureOverride => new TextureSamplingPolicy(
                PreferredProfile: null,
                WrapMode: "Clamp"),
            TextureMemberRole.Albedo
                or TextureMemberRole.Emission => new TextureSamplingPolicy(
                    PreferredProfile: null,
                    WrapMode: null),
            _ => throw new InvalidOperationException($"Unsupported texture member role '{role}'."),
        };
    }

    public static Dictionary<string, Member> CreateTextureMembers(Uri assetUri, TextureMemberRole role)
    {
        ArgumentNullException.ThrowIfNull(assetUri);
        TextureSamplingPolicy samplingPolicy = GetTextureSamplingPolicy(role);

        Dictionary<string, Member> members = new(StringComparer.Ordinal)
        {
            ["URL"] = new Field_Uri
            {
                Value = assetUri,
            },
        };

        if (samplingPolicy.PreferredProfile is not null)
        {
            members["PreferredProfile"] = CreateNullableEnumMember(samplingPolicy.PreferredProfile);
        }

        if (samplingPolicy.WrapMode is not null)
        {
            members["WrapModeU"] = CreateEnumMember(samplingPolicy.WrapMode);
            members["WrapModeV"] = CreateEnumMember(samplingPolicy.WrapMode);
        }

        return members;
    }

    private static bool IsWhiteBundledFamilyMaterial(ResoniteMaterialBinding material)
    {
        return material.TexturePayload is null
            && !string.IsNullOrWhiteSpace(material.Family)
            && IsCodebaseReachableBundledCommonFamily(material.Family!)
            && material.TextureSourceKind == ResoniteTextureSourceKind.Bundled
            && material.DepthOffset is null
            && IsWhiteBaseColor(material.BaseColor);
    }

    private static bool IsCodebaseReachableBundledCommonFamily(string family)
    {
        return string.Equals(family, BundledDefaultMaterialFamilies.Roof, StringComparison.Ordinal)
            || string.Equals(family, BundledDefaultMaterialFamilies.RoadUv, StringComparison.Ordinal)
            || string.Equals(family, BundledDefaultMaterialFamilies.RoadTriplanar, StringComparison.Ordinal)
            || string.Equals(family, BundledDefaultMaterialFamilies.Vegetation, StringComparison.Ordinal)
            || string.Equals(family, BundledDefaultMaterialFamilies.CityFurniture, StringComparison.Ordinal)
            || string.Equals(family, BundledDefaultMaterialFamilies.Other, StringComparison.Ordinal)
            || BundledDefaultMaterialFamilies.BuildingFacadeFamilies.Contains(family, StringComparer.Ordinal);
    }

    private static string CreateCompactColorSuffix(ResoniteColor color)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{color.R:0.###}-{color.G:0.###}-{color.B:0.###}-{color.A:0.###}");
    }

    private static string CreateCommonMaterialSlotName(ResoniteMaterialBinding material)
    {
        string projectionName = ProjectionToken(material.Projection);
        if (material.MaterialType == ResoniteMaterialType.VertexColor)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{projectionName}{TerrainAlignedSuffix(material.DepthOffset)}");
        }

        if (string.IsNullOrWhiteSpace(material.Family))
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{projectionName}{TerrainAlignedSuffix(material.DepthOffset)}");
        }

        int variantIndex = material.BundledVariantIndex ?? 0;
        return BundledDefaultMaterialFamilies.GetVariantMaterialName(material.Family!, variantIndex);
    }

    private static string CreateTerrainOverlayToken(TerrainTextureOverlay terrainTextureOverlay)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureOverlay);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"terrain-overlay-{terrainTextureOverlay.PackageName.ToLowerInvariant()}-{terrainTextureOverlay.SourceDescriptorKey}-bounds-{FormatBounds(terrainTextureOverlay.GeographicBounds)}");
    }

    private static string ProjectionToken(ResoniteMaterialProjection projection)
    {
        return projection switch
        {
            ResoniteMaterialProjection.Uv => "uv",
            ResoniteMaterialProjection.Triplanar => "triplanar",
            _ => projection.ToString().ToLowerInvariant(),
        };
    }

    private static string FormatBounds(GeographicRectangle bounds) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{FormatRounded(bounds.MinLatitude)}-{FormatRounded(bounds.MaxLatitude)}-{FormatRounded(bounds.MinLongitude)}-{FormatRounded(bounds.MaxLongitude)}");

    private static string FormatRounded(double value)
    {
        double rounded = Math.Round(value, 6, MidpointRounding.AwayFromZero);
        return (rounded == 0.0 ? 0.0 : rounded).ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static bool IsWhiteBaseColor(ResoniteColor color)
    {
        return ResoniteMaterialSharing.IsWhiteBaseColor(color);
    }

    private static BundledDefaultMaterialProfile GetBundledDefaultProfile(ResoniteMaterialBinding material)
    {
        string bundledVariantPath = BundledDefaultMaterialFamilies.GetVariant(material.Family!, material.BundledVariantIndex ?? 0);
        return BundledDefaultMaterialProfiles.GetProfile(bundledVariantPath);
    }

    private static ResoniteFloat2? TryGetBundledDefaultScale(ResoniteMaterialBinding material)
    {
        if (string.IsNullOrWhiteSpace(material.Family))
        {
            return null;
        }

        BundledDefaultMaterialProfile defaultProfile = GetBundledDefaultProfile(material);
        return new ResoniteFloat2(defaultProfile.TextureScale.X, defaultProfile.TextureScale.Y);
    }

    private static ResoniteFloat2? TryGetBundledDefaultOffset(ResoniteMaterialBinding material)
    {
        if (string.IsNullOrWhiteSpace(material.Family))
        {
            return null;
        }

        ScalarPair? defaultOffset = GetBundledDefaultProfile(material).TextureOffset;
        return defaultOffset is null ? null : new ResoniteFloat2(defaultOffset.X, defaultOffset.Y);
    }

    private static bool HasNonDefaultBundledTextureTransform(ResoniteMaterialBinding material)
    {
        ResoniteFloat2? defaultTextureScale = TryGetBundledDefaultScale(material);
        bool hasNonDefaultScale = material.TextureScale is not null
            && (defaultTextureScale is null
                || Math.Abs(material.TextureScale.X - defaultTextureScale.X) > 1e-9
                || Math.Abs(material.TextureScale.Y - defaultTextureScale.Y) > 1e-9);
        ResoniteFloat2? defaultTextureOffset = TryGetBundledDefaultOffset(material);
        ResoniteFloat2? effectiveTextureOffset = material.TextureOffset ?? defaultTextureOffset;
        return hasNonDefaultScale || !AreEquivalentTextureOffsets(effectiveTextureOffset, defaultTextureOffset);
    }

    private static bool AreEquivalentTextureOffsets(ResoniteFloat2? left, ResoniteFloat2? right)
    {
        if (IsZeroTextureOffset(left) && IsZeroTextureOffset(right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return Math.Abs(left.X - right.X) < 1e-9
            && Math.Abs(left.Y - right.Y) < 1e-9;
    }

    private static string TerrainAlignedSuffix(ResoniteMaterialDepthOffset? depthOffset) =>
        depthOffset is null ? string.Empty : "-terrain-aligned";

    private static bool IsZeroTextureOffset(ResoniteFloat2? textureOffset)
    {
        return textureOffset is null
            || (Math.Abs(textureOffset.X) < 1e-9
                && Math.Abs(textureOffset.Y) < 1e-9);
    }

    private static Field_Enum CreateEnumMember(string value)
    {
        return new Field_Enum
        {
            Value = value,
        };
    }

    private static Field_Nullable_Enum CreateNullableEnumMember(string value)
    {
        return new Field_Nullable_Enum
        {
            Value = value,
        };
    }
}
