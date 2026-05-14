using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

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
                ? $"payload-{ComputeShortStableHash(normalizedMaterial.MaterialKey)}"
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
        material = NormalizeCommonMaterialBinding(material);
        if (material.AssetScope != ResoniteMaterialAssetScope.Common)
        {
            return EmptyLookupNames;
        }

        return [CreateMaterialSlotName(material, useCommonMaterialAssets: true)];
    }

    public static ResoniteMaterialBinding NormalizeCommonMaterialBinding(ResoniteMaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (material.AssetScope != ResoniteMaterialAssetScope.Common)
        {
            return material;
        }

        if (IsBundledCommonMaterialCandidate(material))
        {
            string canonicalFamily = string.IsNullOrWhiteSpace(material.Family)
                ? BundledDefaultMaterialFamilies.Other
                : material.Family!;
            int canonicalVariantIndex = material.BundledVariantIndex ?? 0;
            BundledDefaultMaterialProfile defaultProfile = BundledDefaultMaterialProfiles.GetProfile(
                BundledDefaultMaterialFamilies.GetVariant(canonicalFamily, canonicalVariantIndex));
            ResoniteFloat2 defaultTextureScale = new(defaultProfile.TextureScale.X, defaultProfile.TextureScale.Y);
            ResoniteFloat2? defaultTextureOffset = defaultProfile.TextureOffset is null
                ? null
                : new ResoniteFloat2(defaultProfile.TextureOffset.X, defaultProfile.TextureOffset.Y);
            ResoniteFloat2 canonicalTextureScale = material.TextureScale ?? defaultTextureScale;
            ResoniteFloat2? canonicalTextureOffset = material.TextureOffset ?? defaultTextureOffset;
            ResoniteMaterialProjection canonicalProjection = GetBundledCommonMaterialProjection(canonicalFamily);
            return material with
            {
                MaterialKey = CreateCanonicalCommonMaterialKey(
                    canonicalFamily,
                    canonicalVariantIndex),
                BaseColor = new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                MaterialType = ResoniteMaterialType.Standard,
                TextureSourceKind = ResoniteTextureSourceKind.Bundled,
                Projection = canonicalProjection,
                TextureScale = canonicalTextureScale,
                Family = canonicalFamily,
                TextureOffset = canonicalTextureOffset,
                DepthOffset = null,
                BundledVariantIndex = canonicalVariantIndex,
            };
        }

        if (IsGenericSharedCommonMaterialCandidate(material))
        {
            return NormalizeGenericSharedMaterialBinding(material);
        }

        if (ResoniteMaterialSharing.CanUseSharedVertexColorMaterial(material))
        {
            return NormalizeVertexColorSharedMaterialBinding(material);
        }

        return material with { AssetScope = ResoniteMaterialAssetScope.PresentationSlotScoped };
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
        material = NormalizeCommonMaterialBinding(material);
        if (TryGetCommonMaterialPathParts(material.MaterialKey, out string familySlotName, out _))
        {
            return familySlotName;
        }

        return material.MaterialType == ResoniteMaterialType.VertexColor
            ? "vertex-color"
            : string.IsNullOrWhiteSpace(material.Family)
                ? "generic"
                : material.Family!;
    }

    public static bool TryNormalizeSharedMaterialBinding(
        ResoniteMaterialBinding material,
        out ResoniteMaterialBinding normalizedMaterial,
        out string familySlotName)
    {
        ArgumentNullException.ThrowIfNull(material);

        normalizedMaterial = material;
        familySlotName = string.Empty;
        if (ResoniteMaterialSharing.CanUseSharedVertexColorMaterial(material))
        {
            normalizedMaterial = NormalizeVertexColorSharedMaterialBinding(material);
            familySlotName = GetCommonMaterialFamilySlotName(normalizedMaterial);
            return true;
        }

        if (material.MaterialType != ResoniteMaterialType.Standard)
        {
            return false;
        }

        bool isWhiteBundledFamilyMaterial = IsWhiteBundledFamilyMaterial(material);
        if (isWhiteBundledFamilyMaterial
            && material.AssetScope == ResoniteMaterialAssetScope.Common)
        {
            ResoniteMaterialBinding commonBaseCandidate = material with
            {
                BaseColor = new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                TexturePayload = null,
                TextureSourceKind = ResoniteTextureSourceKind.Bundled,
                AssetScope = ResoniteMaterialAssetScope.Common,
            };
            normalizedMaterial = NormalizeCommonMaterialBinding(commonBaseCandidate);
            if (normalizedMaterial.AssetScope != ResoniteMaterialAssetScope.Common)
            {
                return false;
            }

            familySlotName = GetCommonMaterialFamilySlotName(normalizedMaterial);
            return true;
        }

        if (!IsWhiteBaseColor(material.BaseColor))
        {
            return false;
        }

        if (material.Projection != ResoniteMaterialProjection.Uv)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(material.Family))
        {
            return false;
        }

        if (material.TerrainOverlay is not null)
        {
            return false;
        }

        if (HasEffectiveGenericTextureTransform(material))
        {
            return false;
        }

        normalizedMaterial = NormalizeGenericSharedMaterialBinding(material);
        familySlotName = GetCommonMaterialFamilySlotName(normalizedMaterial);
        return true;
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

        if (TryNormalizeSharedMaterialBinding(material, out ResoniteMaterialBinding normalizedSharedMaterial, out _)
            && material.TexturePayload is null)
        {
            return normalizedSharedMaterial with
            {
                SubmeshIndices = material.SubmeshIndices,
                TerrainOverlay = material.TerrainOverlay,
                TerrainMeshCode = material.TerrainMeshCode,
            };
        }

        if (material.MaterialType == ResoniteMaterialType.Standard
            && material.TexturePayload is null
            && material.AssetScope != ResoniteMaterialAssetScope.Common
            && string.IsNullOrWhiteSpace(material.Family))
        {
            return material with
            {
                MaterialKey = "preserved-standard-textureless",
            };
        }

        return material;
    }

    public static string CreateCanonicalCommonMaterialKey(
        string family,
        int bundledVariantIndex)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{family}/{BundledDefaultMaterialFamilies.GetVariantMaterialName(family, bundledVariantIndex)}");
    }

    public static string CreateCanonicalGenericSharedMaterialKey(
        ResoniteMaterialProjection projection,
        ResoniteFloat2? textureScale,
        ResoniteFloat2? textureOffset,
        ResoniteMaterialDepthOffset? depthOffset)
    {
        return ResoniteMaterialSharing.CreateCanonicalGenericSharedMaterialKey(
            projection,
            textureScale,
            textureOffset,
            depthOffset);
    }

    public static string CreateCanonicalVertexColorCommonMaterialKey(
        ResoniteMaterialProjection projection,
        ResoniteMaterialDepthOffset? depthOffset)
    {
        return ResoniteMaterialSharing.CreateCanonicalVertexColorCommonMaterialKey(projection, depthOffset);
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

    private static bool IsBundledCommonMaterialCandidate(ResoniteMaterialBinding material)
    {
        return material.TerrainOverlay is null
            && material.TexturePayload is null
            && material.TextureSourceKind == ResoniteTextureSourceKind.Bundled
            && !string.IsNullOrWhiteSpace(material.Family)
            && IsCodebaseReachableBundledCommonFamily(material.Family!)
            && material.DepthOffset is null
            && !HasNonDefaultBundledTextureTransform(material)
            && IsWhiteBaseColor(material.BaseColor);
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

    private static bool IsGenericSharedCommonMaterialCandidate(ResoniteMaterialBinding material)
    {
        return material.MaterialType == ResoniteMaterialType.Standard
            && material.Projection == ResoniteMaterialProjection.Uv
            && ResoniteMaterialSharing.IsWhiteBaseColor(material.BaseColor)
            && string.IsNullOrWhiteSpace(material.Family)
            && material.TextureSourceKind == ResoniteTextureSourceKind.Dataset
            && material.TerrainOverlay is null;
    }

    private static string CreateCompactColorSuffix(ResoniteColor color)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{color.R:0.###}-{color.G:0.###}-{color.B:0.###}-{color.A:0.###}");
    }

    private static string CreateCommonMaterialSlotName(ResoniteMaterialBinding material)
    {
        if (TryGetCommonMaterialPathParts(material.MaterialKey, out _, out string materialSlotName))
        {
            return materialSlotName;
        }

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

    private static string ComputeShortStableHash(string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant();
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

    private static string FormatFloat2(ResoniteFloat2? value)
    {
        return value is null
            ? "none"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{FormatRounded(value.X)}-{FormatRounded(value.Y)}");
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

    private static bool TryGetCommonMaterialPathParts(
        string materialKey,
        out string familySlotName,
        out string materialSlotName)
    {
        int separatorIndex = materialKey.IndexOf('/');
        if (separatorIndex <= 0 || separatorIndex == materialKey.Length - 1)
        {
            familySlotName = string.Empty;
            materialSlotName = string.Empty;
            return false;
        }

        familySlotName = materialKey[..separatorIndex];
        materialSlotName = materialKey[(separatorIndex + 1)..];
        return true;
    }

    private static string TerrainAlignedSuffix(ResoniteMaterialDepthOffset? depthOffset) =>
        depthOffset is null ? string.Empty : "-terrain-aligned";

    private static ResoniteMaterialBinding NormalizeGenericSharedMaterialBinding(ResoniteMaterialBinding material)
    {
        ResoniteFloat2? normalizedTextureScale = material.TextureScale is not null
            && Math.Abs(material.TextureScale.X - 1.0) < 1e-9
            && Math.Abs(material.TextureScale.Y - 1.0) < 1e-9
            ? null
            : material.TextureScale;
        ResoniteFloat2? normalizedTextureOffset = IsZeroTextureOffset(material.TextureOffset)
            ? null
            : material.TextureOffset;

        return material with
        {
            MaterialKey = CreateCanonicalGenericSharedMaterialKey(
                material.Projection,
                normalizedTextureScale,
                normalizedTextureOffset,
                material.DepthOffset),
            BaseColor = new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            TexturePayload = null,
            TerrainOverlay = null,
            TextureSourceKind = ResoniteTextureSourceKind.Dataset,
            TextureScale = normalizedTextureScale,
            TextureOffset = normalizedTextureOffset,
            AssetScope = ResoniteMaterialAssetScope.Common,
            BundledVariantIndex = null,
            TerrainMeshCode = null,
        };
    }

    private static ResoniteMaterialBinding NormalizeVertexColorSharedMaterialBinding(ResoniteMaterialBinding material)
    {
        return material with
        {
            MaterialKey = CreateCanonicalVertexColorCommonMaterialKey(
                material.Projection,
                material.DepthOffset),
            BaseColor = new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            TexturePayload = null,
            TerrainOverlay = null,
            TextureSourceKind = ResoniteTextureSourceKind.Bundled,
            Family = null,
            TextureScale = null,
            TextureOffset = null,
            AssetScope = ResoniteMaterialAssetScope.Common,
            BundledVariantIndex = null,
        };
    }

    private static bool HasEffectiveGenericTextureTransform(ResoniteMaterialBinding material)
    {
        bool hasNonIdentityScale = material.TextureScale is not null
            && (Math.Abs(material.TextureScale.X - 1.0) > 1e-9
                || Math.Abs(material.TextureScale.Y - 1.0) > 1e-9);
        return hasNonIdentityScale || !IsZeroTextureOffset(material.TextureOffset);
    }

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
