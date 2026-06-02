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

    internal enum PlannedTextureRole
    {
        Albedo,
        Normal,
        Height,
        Metallic,
        Emission,
    }

    internal readonly record struct TextureSamplingPolicy(
        string? PreferredProfile,
        string? WrapMode);

    public static string CreateMaterialSlotName(ResoniteMaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(material);
        ResoniteMaterialBinding normalizedMaterial = ResoniteDynamicMaterialUvNormalizer.NormalizeMaterialBinding(material);

        return CreateCommonMaterialSlotName(normalizedMaterial);
    }

    public static string CreateDedicatedMaterialSlotName(ResoniteMaterialBinding material, int materialIndex)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentOutOfRangeException.ThrowIfNegative(materialIndex);
        ResoniteMaterialBinding normalizedMaterial = ResoniteDynamicMaterialUvNormalizer.NormalizeMaterialBinding(material);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"material-{materialIndex:000}-{MaterialComponentToken(normalizedMaterial)}-{ProjectionToken(normalizedMaterial.Projection)}");
    }

    public static IReadOnlyList<string> CreateCommonMaterialSlotLookupNames(ResoniteMaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (material.AssetScope != ResoniteMaterialAssetScope.Common)
        {
            return EmptyLookupNames;
        }

        return [CreateMaterialSlotName(material)];
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
            && material.CommonMaterial is null
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

    internal static bool ShouldDemoteBundledCommonMaterial(ResoniteMaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(material);

        return material.AssetScope == ResoniteMaterialAssetScope.Common
            && material.MaterialType == ResoniteMaterialType.Standard
            && material.TexturePayload is null
            && material.TerrainOverlay is null
            && material.TextureSourceKind == ResoniteTextureSourceKind.Bundled
            && !string.IsNullOrWhiteSpace(material.Family)
            && (!IsWhiteBaseColor(material.BaseColor)
                || HasNonDefaultBundledTextureTransform(material)
                || material.DepthOffset is not null);
    }

    public static TextureIdentity CreateTextureIdentity(PlannedTextureRole role)
    {
        return new TextureIdentity(role switch
        {
            PlannedTextureRole.Albedo => "albedo",
            PlannedTextureRole.Normal => "normal",
            PlannedTextureRole.Height => "height",
            PlannedTextureRole.Metallic => "metallic",
            PlannedTextureRole.Emission => "emission",
            _ => throw new InvalidOperationException($"Planned texture role '{role}' is unsupported."),
        });
    }

    public static TextureMemberRole ToTextureMemberRole(PlannedTextureRole role)
    {
        return role switch
        {
            PlannedTextureRole.Albedo => TextureMemberRole.Albedo,
            PlannedTextureRole.Normal => TextureMemberRole.Normal,
            PlannedTextureRole.Height => TextureMemberRole.Height,
            PlannedTextureRole.Metallic => TextureMemberRole.Metallic,
            PlannedTextureRole.Emission => TextureMemberRole.Emission,
            _ => throw new InvalidOperationException($"Planned texture role '{role}' is unsupported."),
        };
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

    private static string ProjectionToken(ResoniteMaterialProjection projection)
    {
        return projection switch
        {
            ResoniteMaterialProjection.Uv => "uv",
            ResoniteMaterialProjection.Triplanar => "triplanar",
            _ => projection.ToString().ToLowerInvariant(),
        };
    }

    private static string MaterialComponentToken(ResoniteMaterialBinding material)
    {
        return material.MaterialType switch
        {
            ResoniteMaterialType.Standard => material.Projection switch
            {
                ResoniteMaterialProjection.Uv => "pbs-uv",
                ResoniteMaterialProjection.Triplanar => "pbs-triplanar",
                _ => "material",
            },
            ResoniteMaterialType.VertexColor => "vertex-color",
            ResoniteMaterialType.Wireframe => "wireframe",
            _ => "material",
        };
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
