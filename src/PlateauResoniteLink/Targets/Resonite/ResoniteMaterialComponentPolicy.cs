using System;
using System.Collections.Generic;

using PlateauResoniteLink.Domain.Importing;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class ResoniteMaterialComponentPolicy
{
    private static readonly ResoniteFloat2 DefaultTriplanarTextureScale = new(
        BundledDefaultMaterialTiling.DefaultTilesPerMeterValue.X,
        BundledDefaultMaterialTiling.DefaultTilesPerMeterValue.Y);
    private const float DefaultWireframeThickness = 0.01f;
    private const double DefaultWireframeFillOpacity = 0.08;

    public static string GetComponentType(ResoniteMaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(material);

        return material.MaterialType switch
        {
            ResoniteMaterialType.Standard => material.Projection switch
            {
                ResoniteMaterialProjection.Uv => "[FrooxEngine]FrooxEngine.PBS_Metallic",
                ResoniteMaterialProjection.Triplanar => "[FrooxEngine]FrooxEngine.PBS_TriplanarMetallic",
                _ => throw new InvalidOperationException($"Unsupported material projection '{material.Projection}'."),
            },
            ResoniteMaterialType.VertexColor => "[FrooxEngine]FrooxEngine.PBS_VertexColorMetallic",
            ResoniteMaterialType.Wireframe => "[FrooxEngine]FrooxEngine.WireframeMaterial",
            _ => throw new InvalidOperationException($"Unsupported material type '{material.MaterialType}'."),
        };
    }

    public static Dictionary<string, Member> CreateMembers(ResoniteMaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(material);
        ValidateNoNonCommonUvTransform(material);

        Dictionary<string, Member> materialMembers = new(StringComparer.Ordinal);

        if (material.MaterialType == ResoniteMaterialType.Standard)
        {
            materialMembers["AlbedoColor"] = CreateColorMember(material.BaseColor);
            materialMembers["Smoothness"] = new Field_float
            {
                Value = 0.0f,
            };
        }

        if (material.MaterialType == ResoniteMaterialType.VertexColor)
        {
            materialMembers["AlbedoColor"] = CreateColorMember(new ResoniteColor(1.0, 1.0, 1.0, 1.0));
            materialMembers["Smoothness"] = new Field_float
            {
                Value = 0.0f,
            };
        }

        if (material.MaterialType == ResoniteMaterialType.Standard
            && material.Projection == ResoniteMaterialProjection.Uv
            && (material.TextureScale is not null || material.TextureOffset is not null)
            && !ShouldOmitUvTransformMembers(material))
        {
            AddTextureTransformMembers(materialMembers, material.TextureScale ?? new ResoniteFloat2(1.0, 1.0), material.TextureOffset);
        }

        if (material.MaterialType == ResoniteMaterialType.Standard
            && material.Projection == ResoniteMaterialProjection.Triplanar)
        {
            AddTextureTransformMembers(
                materialMembers,
                material.TextureScale ?? DefaultTriplanarTextureScale,
                material.TextureOffset);
            materialMembers["Metallic"] = new Field_float
            {
                Value = 0.0f,
            };
            materialMembers["TriplanarBlendPower"] = new Field_float
            {
                Value = 8.0f,
            };
            materialMembers["ObjectSpace"] = new Field_bool
            {
                Value = true,
            };
        }

        if (material.MaterialType == ResoniteMaterialType.Wireframe)
        {
            materialMembers["Thickness"] = new Field_float
            {
                Value = DefaultWireframeThickness,
            };
            materialMembers["ScreenSpace"] = new Field_bool
            {
                Value = true,
            };
            materialMembers["LineColor"] = CreateColorMember(material.BaseColor);
            materialMembers["FillColor"] = CreateColorMember(material.BaseColor with
            {
                A = Math.Clamp(material.BaseColor.A * DefaultWireframeFillOpacity, 0.0, 1.0),
            });
            materialMembers["DoubleSided"] = new Field_bool
            {
                Value = true,
            };
        }

        if (material.DepthOffset is not null)
        {
            materialMembers["OffsetFactor"] = new Field_float
            {
                Value = (float)material.DepthOffset.Factor,
            };
            materialMembers["OffsetUnits"] = new Field_float
            {
                Value = (float)material.DepthOffset.Units,
            };
        }

        return materialMembers;
    }

    internal static string DescribeForDiagnostics(ResoniteMaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(material);

        string textureShape = material.TexturePayload is not null
            ? "texture-payload"
            : material.TerrainOverlay is not null
                ? "terrain-overlay"
                : "no-texture";
        string textureTransform = material.TextureScale is null && material.TextureOffset is null
            ? "identity"
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"scale={FormatFloat2(material.TextureScale)} offset={FormatFloat2(material.TextureOffset)}");

        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"type={material.MaterialType}, projection={material.Projection}, assetScope={material.AssetScope}, family={material.Family ?? "none"}, texture={textureShape}, transform={textureTransform}");
    }

    private static void AddTextureTransformMembers(
        Dictionary<string, Member> materialMembers,
        ResoniteFloat2 textureScale,
        ResoniteFloat2? textureOffset)
    {
        materialMembers["TextureScale"] = new Field_float2
        {
            Value = new float2
            {
                x = (float)textureScale.X,
                y = (float)textureScale.Y,
            },
        };
        materialMembers["TextureOffset"] = new Field_float2
        {
            Value = new float2
            {
                x = (float)(textureOffset?.X ?? 0.0),
                y = (float)(textureOffset?.Y ?? 0.0),
            },
        };
    }

    public static bool TryGetBundledCompanionTextureSet(
        BundledDefaultMaterialAssetStore bundledDefaultMaterialAssetStore,
        ResoniteMaterialBinding material,
        out BundledDefaultMaterialTextureSet? textureSet)
    {
        ArgumentNullException.ThrowIfNull(bundledDefaultMaterialAssetStore);
        ArgumentNullException.ThrowIfNull(material);

        textureSet = null;
        if (material.MaterialType != ResoniteMaterialType.Standard
            || material.TextureSourceKind != ResoniteTextureSourceKind.Bundled
            || string.IsNullOrWhiteSpace(material.Family))
        {
            return false;
        }

        BundledDefaultMaterialVariant variant = BundledDefaultMaterialFamilies.GetVariantDefinition(
            material.Family!,
            material.BundledVariantIndex ?? 0);
        textureSet = new BundledDefaultMaterialTextureSet(
            ResolveEmissionTextureSource(bundledDefaultMaterialAssetStore, variant),
            ResolveTextureSource<BundledDefaultHeightTextureRole>(bundledDefaultMaterialAssetStore, variant, variant.TextureSources?.Height),
            ResolveTextureSource<BundledDefaultMetallicTextureRole>(bundledDefaultMaterialAssetStore, variant, variant.TextureSources?.Metallic),
            ResolveTextureSource<BundledDefaultNormalTextureRole>(bundledDefaultMaterialAssetStore, variant, variant.TextureSources?.Normal));
        return true;
    }

    private static BundledDefaultTextureAsset<BundledDefaultEmissionTextureRole>? ResolveEmissionTextureSource(
        BundledDefaultMaterialAssetStore bundledDefaultMaterialAssetStore,
        BundledDefaultMaterialVariant variant)
    {
        if (variant.TextureSources?.Emission is { } explicitEmission)
        {
            if (BundledDefaultTextureAssets.IsBlackEmission(explicitEmission))
            {
                return null;
            }

            EnsureBundledTextureExists(bundledDefaultMaterialAssetStore, explicitEmission);
            return explicitEmission;
        }

        return ResolveTextureSource<BundledDefaultEmissionTextureRole>(
            bundledDefaultMaterialAssetStore,
            variant,
            explicitAsset: null);
    }

    private static BundledDefaultTextureAsset<TRole>? ResolveTextureSource<TRole>(
        BundledDefaultMaterialAssetStore bundledDefaultMaterialAssetStore,
        BundledDefaultMaterialVariant variant,
        BundledDefaultTextureAsset<TRole>? explicitAsset)
        where TRole : IBundledDefaultTextureRole
    {
        if (explicitAsset is not null)
        {
            EnsureBundledTextureExists(bundledDefaultMaterialAssetStore, explicitAsset);
            return explicitAsset;
        }

        if (!BundledDefaultTextureAssets.TryGetCompanionAsset(variant.Albedo, out BundledDefaultTextureAsset<TRole>? companion))
        {
            return null;
        }

        return bundledDefaultMaterialAssetStore.TryGetAbsolutePath(companion!, out _)
            ? companion
            : null;
    }

    private static void EnsureBundledTextureExists(
        BundledDefaultMaterialAssetStore bundledDefaultMaterialAssetStore,
        BundledDefaultTextureAsset asset)
    {
        if (!bundledDefaultMaterialAssetStore.TryGetAbsolutePath(asset, out _))
        {
            throw new InvalidOperationException("Could not resolve bundled texture source.");
        }
    }

    private static bool ShouldOmitUvTransformMembers(ResoniteMaterialBinding material)
    {
        if (material.AssetScope == ResoniteMaterialAssetScope.Common
            && string.IsNullOrWhiteSpace(material.Family)
            && material.TerrainOverlay is null)
        {
            return true;
        }

        return ResoniteDynamicMaterialUvNormalizer.ShouldNormalizeTextureTransform(material)
            && material.TextureScale is not null
            && (Math.Abs(material.TextureScale.X - 1.0) > 1e-9
                || Math.Abs(material.TextureScale.Y - 1.0) > 1e-9);
    }

    private static void ValidateNoNonCommonUvTransform(ResoniteMaterialBinding material)
    {
        if (material.MaterialType != ResoniteMaterialType.Standard
            || material.Projection != ResoniteMaterialProjection.Uv
            || material.AssetScope == ResoniteMaterialAssetScope.Common)
        {
            return;
        }

        bool hasTextureScale = material.TextureScale is not null;
        bool hasTextureOffset = material.TextureOffset is not null;
        if (!hasTextureScale && !hasTextureOffset)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Non-common UV material ({DescribeForDiagnostics(material)}) reached Resonite material emission with TextureScale/TextureOffset. "
            + "Normalize city-object UV transforms into mesh UVs before emission.");
    }

    private static string FormatFloat2(ResoniteFloat2? value)
    {
        return value is null
            ? "none"
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"({value.X:0.######},{value.Y:0.######})");
    }

    public static Field_colorX CreateColorMember(ResoniteColor color)
    {
        return ResoniteColorSpace.CreateSrgbColorMember(color);
    }

}

internal sealed record BundledDefaultMaterialTextureSet(
    BundledDefaultTextureAsset<BundledDefaultEmissionTextureRole>? Emission,
    BundledDefaultTextureAsset<BundledDefaultHeightTextureRole>? Height,
    BundledDefaultTextureAsset<BundledDefaultMetallicTextureRole>? Metallic,
    BundledDefaultTextureAsset<BundledDefaultNormalTextureRole>? Normal);
