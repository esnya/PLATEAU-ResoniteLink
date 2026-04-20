using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using PlateauResoniteLink.Domain.Importing;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class ResoniteSceneMaterialConventions
{
    public static string CreateMaterialSlotName(ResoniteMaterialBinding material, bool useCommonMaterialAssets)
    {
        ArgumentNullException.ThrowIfNull(material);

        if (useCommonMaterialAssets)
        {
            return CreateCommonMaterialSlotName(material);
        }

        string componentKind = material.MaterialType switch
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

        string projectionName = material.Projection switch
        {
            ResoniteMaterialProjection.Uv => "uv",
            ResoniteMaterialProjection.Triplanar => "triplanar",
            _ => material.Projection.ToString().ToLowerInvariant(),
        };

        string sourceName = material.TerrainOverlay is not null
            ? CreateTerrainOverlayToken(material.TerrainOverlay)
            : material.TexturePayload is not null
                ? $"payload-{ComputeShortStableHash(material.MaterialKey)}"
            : material.AssetScope == ResoniteMaterialAssetScope.Common
                ? $"bundled-v{material.BundledVariantIndex ?? 0}"
            : material.MaterialType.ToString();

        string familyName = string.IsNullOrWhiteSpace(material.Family)
            ? "none"
            : material.Family!;
        string colorName = CreateCompactColorSuffix(material.BaseColor);
        string scaleName = material.TextureScale is not null
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{material.TextureScale.X:0.######}x{material.TextureScale.Y:0.######}")
            : "none";
        string offsetName = material.TextureOffset is not null
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{material.TextureOffset.X:0.######}x{material.TextureOffset.Y:0.######}")
            : "none";
        string depthName = material.DepthOffset is not null
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{material.DepthOffset.Factor:0.######}x{material.DepthOffset.Units:0.######}")
            : "none";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{componentKind}_{projectionName}_{sourceName}_{familyName}_{scaleName}_{offsetName}_{depthName}_{colorName}");
    }

    public static ResoniteMaterialBinding NormalizeCommonMaterialBinding(ResoniteMaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (material.AssetScope != ResoniteMaterialAssetScope.Common)
        {
            return material;
        }

        if (!IsBundledCommonMaterialCandidate(material))
        {
            return material with { AssetScope = ResoniteMaterialAssetScope.PresentationSlotScoped };
        }

        string canonicalFamily = string.IsNullOrWhiteSpace(material.Family)
            ? BundledDefaultMaterialFamilies.Other
            : material.Family!;
        int canonicalVariantIndex = material.BundledVariantIndex ?? 0;
        ResoniteFloat2 defaultTextureScale = BundledDefaultMaterialProfiles.GetTilesPerMeter(
            BundledDefaultMaterialFamilies.GetVariant(canonicalFamily, canonicalVariantIndex));
        ResoniteFloat2 canonicalTextureScale = material.TextureScale ?? defaultTextureScale;
        return material with
        {
            MaterialKey = CreateCanonicalCommonMaterialKey(
                canonicalFamily,
                canonicalVariantIndex,
                material.Projection,
                canonicalTextureScale),
            BaseColor = new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType = ResoniteMaterialType.Standard,
            TextureSourceKind = ResoniteTextureSourceKind.Bundled,
            TextureScale = canonicalTextureScale,
            Family = canonicalFamily,
            TextureOffset = null,
            DepthOffset = null,
            BundledVariantIndex = canonicalVariantIndex,
        };
    }

    public static bool TryNormalizeSharedMaterialBinding(
        ResoniteMaterialBinding material,
        out ResoniteMaterialBinding normalizedMaterial,
        out string familySlotName)
    {
        ArgumentNullException.ThrowIfNull(material);

        normalizedMaterial = material;
        familySlotName = string.Empty;
        if (material.MaterialType != ResoniteMaterialType.Standard)
        {
            return false;
        }

        if (material.TexturePayload is null
            && material.TerrainOverlay is null
            && !string.IsNullOrWhiteSpace(material.Family)
            && material.TextureSourceKind == ResoniteTextureSourceKind.Bundled
            && material.DepthOffset is null
            && material.TextureOffset is null)
        {
            ResoniteMaterialBinding commonBaseCandidate = material with
            {
                BaseColor = new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                TexturePayload = null,
                TerrainOverlay = null,
                TextureSourceKind = ResoniteTextureSourceKind.Bundled,
                AssetScope = ResoniteMaterialAssetScope.Common,
            };
            normalizedMaterial = NormalizeCommonMaterialBinding(commonBaseCandidate);
            if (normalizedMaterial.AssetScope != ResoniteMaterialAssetScope.Common)
            {
                return false;
            }

            familySlotName = normalizedMaterial.Family ?? BundledDefaultMaterialFamilies.Other;
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

        normalizedMaterial = NormalizeGenericSharedMaterialBinding(material);
        familySlotName = "generic";
        return true;
    }

    public static ResoniteMaterialBinding NormalizeBatchGroupedMaterialBinding(ResoniteMaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(material);

        if (TryNormalizeSharedMaterialBinding(material, out ResoniteMaterialBinding normalizedSharedMaterial, out _)
            && !string.IsNullOrWhiteSpace(normalizedSharedMaterial.Family)
            && material.TexturePayload is null
            && material.TerrainOverlay is null)
        {
            return normalizedSharedMaterial with
            {
                SubmeshIndices = material.SubmeshIndices,
            };
        }

        if (material.MaterialType == ResoniteMaterialType.VertexColor)
        {
            return material with
            {
                MaterialKey = "preserved-vertex-color",
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
        int bundledVariantIndex,
        ResoniteMaterialProjection projection,
        ResoniteFloat2? textureScale)
    {
        string scaleToken = textureScale is null
            ? "none"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{textureScale.X:0.######}x{textureScale.Y:0.######}");
        return string.Create(
            CultureInfo.InvariantCulture,
            $"common|{family}|variant:{bundledVariantIndex}|{projection}|scale:{scaleToken}");
    }

    public static string CreateCanonicalGenericSharedMaterialKey(
        ResoniteMaterialProjection projection,
        ResoniteFloat2? textureScale,
        ResoniteFloat2? textureOffset,
        ResoniteMaterialDepthOffset? depthOffset)
    {
        string scaleToken = textureScale is null
            ? "none"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{textureScale.X:0.######}x{textureScale.Y:0.######}");
        string offsetToken = textureOffset is null
            ? "none"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{textureOffset.X:0.######}x{textureOffset.Y:0.######}");
        string depthToken = depthOffset is null
            ? "none"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{depthOffset.Factor:0.######}x{depthOffset.Units:0.######}");
        return string.Create(
            CultureInfo.InvariantCulture,
            $"generic|{projection}|scale:{scaleToken}|offset:{offsetToken}|depth:{depthToken}");
    }

    public static Dictionary<string, Member> CreateTextureMembers(Uri assetUri)
    {
        return new Dictionary<string, Member>(StringComparer.Ordinal)
        {
            ["URL"] = new Field_Uri
            {
                Value = assetUri,
            },
        };
    }

    private static bool IsBundledCommonMaterialCandidate(ResoniteMaterialBinding material)
    {
        return material.TerrainOverlay is null
            && material.TexturePayload is null
            && material.TextureSourceKind == ResoniteTextureSourceKind.Bundled
            && !string.IsNullOrWhiteSpace(material.Family);
    }

    private static string CreateCompactColorSuffix(ResoniteColor color)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{color.R:0.###}-{color.G:0.###}-{color.B:0.###}-{color.A:0.###}");
    }

    private static string CreateCommonMaterialSlotName(ResoniteMaterialBinding material)
    {
        string projectionName = material.Projection switch
        {
            ResoniteMaterialProjection.Uv => "uv",
            ResoniteMaterialProjection.Triplanar => "triplanar",
            _ => material.Projection.ToString().ToLowerInvariant(),
        };
        if (string.IsNullOrWhiteSpace(material.Family))
        {
            string genericScaleToken = material.TextureScale is null
                ? string.Empty
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"_scale_{material.TextureScale.X:0.######}x{material.TextureScale.Y:0.######}");
            string offsetToken = material.TextureOffset is null
                ? string.Empty
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"_offset_{material.TextureOffset.X:0.######}x{material.TextureOffset.Y:0.######}");
            string depthToken = material.DepthOffset is null
                ? string.Empty
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"_depth_{material.DepthOffset.Factor:0.######}x{material.DepthOffset.Units:0.######}");
            return string.Create(
                CultureInfo.InvariantCulture,
                $"shared_{projectionName}_generic{genericScaleToken}{offsetToken}{depthToken}");
        }

        int variantIndex = material.BundledVariantIndex ?? 0;
        ResoniteFloat2? defaultTextureScale = TryGetBundledDefaultScale(material);
        ResoniteFloat2? materialTextureScale = material.TextureScale;
        bool hasNonDefaultScale = materialTextureScale is not null
            && (defaultTextureScale is null
                || Math.Abs(materialTextureScale.X - defaultTextureScale.X) > 1e-9
                || Math.Abs(materialTextureScale.Y - defaultTextureScale.Y) > 1e-9);
        string scaleToken = hasNonDefaultScale
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"_scale_{materialTextureScale!.X:0.######}x{materialTextureScale.Y:0.######}")
            : string.Empty;
        string variantNameToken = TryCreateBundledVariantNameToken(material);
        string variantNameSuffix = string.IsNullOrWhiteSpace(variantNameToken)
            ? string.Empty
            : $"_{variantNameToken}";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"shared_{projectionName}_variant_{variantIndex}{scaleToken}{variantNameSuffix}");
    }

    private static string CreateTerrainOverlayToken(TerrainTextureOverlay terrainTextureOverlay)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureOverlay);

        string source = string.Create(
            CultureInfo.InvariantCulture,
            $"{terrainTextureOverlay.PackageName}|{terrainTextureOverlay.GeographicBounds.MinLatitude:0.######}|{terrainTextureOverlay.GeographicBounds.MinLongitude:0.######}");
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"terrain-overlay-{Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant()}");
    }

    private static string ComputeShortStableHash(string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant();
    }

    private static bool IsWhiteBaseColor(ResoniteColor color)
    {
        return Math.Abs(color.R - 1.0) < 1e-9
            && Math.Abs(color.G - 1.0) < 1e-9
            && Math.Abs(color.B - 1.0) < 1e-9
            && Math.Abs(color.A - 1.0) < 1e-9;
    }

    private static ResoniteFloat2? TryGetBundledDefaultScale(ResoniteMaterialBinding material)
    {
        if (string.IsNullOrWhiteSpace(material.Family))
        {
            return null;
        }

        string bundledVariantPath = BundledDefaultMaterialFamilies.GetVariant(material.Family!, material.BundledVariantIndex ?? 0);
        return BundledDefaultMaterialProfiles.GetTilesPerMeter(bundledVariantPath);
    }

    private static string TryCreateBundledVariantNameToken(ResoniteMaterialBinding material)
    {
        if (string.IsNullOrWhiteSpace(material.Family))
        {
            return string.Empty;
        }

        string fileName = Path.GetFileNameWithoutExtension(
            BundledDefaultMaterialFamilies.GetVariant(material.Family!, material.BundledVariantIndex ?? 0));
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        StringBuilder builder = new(fileName.Length);
        foreach (char character in fileName)
        {
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-');
        }

        return builder.ToString().Trim('-');
    }

    private static ResoniteMaterialBinding NormalizeGenericSharedMaterialBinding(ResoniteMaterialBinding material)
    {
        return material with
        {
            MaterialKey = CreateCanonicalGenericSharedMaterialKey(
                material.Projection,
                material.TextureScale,
                material.TextureOffset,
                material.DepthOffset),
            BaseColor = new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            TexturePayload = null,
            TerrainOverlay = null,
            TextureSourceKind = ResoniteTextureSourceKind.Dataset,
            AssetScope = ResoniteMaterialAssetScope.Common,
            BundledVariantIndex = null,
        };
    }
}
