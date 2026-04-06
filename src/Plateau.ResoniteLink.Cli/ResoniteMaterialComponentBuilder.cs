using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Cli;

internal static class ResoniteMaterialComponentBuilder
{
    private static readonly ResoniteFloat2 DefaultTriplanarTextureScale = BundledDefaultMaterialTiling.DefaultTilesPerMeter;
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
            && material.TextureScale is not null)
        {
            materialMembers["TextureScale"] = new Field_float2
            {
                Value = new float2
                {
                    x = (float)material.TextureScale.X,
                    y = (float)material.TextureScale.Y,
                },
            };
            materialMembers["TextureOffset"] = new Field_float2
            {
                Value = new float2
                {
                    x = 0.0f,
                    y = 0.0f,
                },
            };
        }

        if (material.MaterialType == ResoniteMaterialType.Standard
            && material.Projection == ResoniteMaterialProjection.Triplanar)
        {
            materialMembers["TextureScale"] = new Field_float2
            {
                Value = new float2
                {
                    x = (float)DefaultTriplanarTextureScale.X,
                    y = (float)DefaultTriplanarTextureScale.Y,
                },
            };
            materialMembers["TextureOffset"] = new Field_float2
            {
                Value = new float2
                {
                    x = 0.0f,
                    y = 0.0f,
                },
            };
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

    public static bool TryGetBundledCompanionTextureSet(
        ResoniteMaterialBinding material,
        out BundledDefaultMaterialTextureSet? textureSet)
    {
        ArgumentNullException.ThrowIfNull(material);

        textureSet = null;
        if (material.MaterialType != ResoniteMaterialType.Standard
            || material.TextureSourceKind != ResoniteTextureSourceKind.Bundled
            || string.IsNullOrWhiteSpace(material.TexturePath))
        {
            return false;
        }

        string albedoLogicalPath = material.TexturePath;
        string stem = Path.GetFileNameWithoutExtension(albedoLogicalPath);
        if (!stem.EndsWith("_Color", StringComparison.Ordinal))
        {
            return false;
        }

        string directory = Path.GetDirectoryName(albedoLogicalPath)?.Replace('\\', '/')
            ?? throw new InvalidOperationException($"Could not determine bundled texture directory for '{albedoLogicalPath}'.");
        string baseStem = stem[..^"_Color".Length];

        textureSet = new BundledDefaultMaterialTextureSet(
            TryResolveBundledTexture(directory, $"{baseStem}_Emission.jpg"),
            TryResolveBundledTexture(directory, $"{baseStem}_Height.jpg"),
            TryResolveBundledTexture(directory, $"{baseStem}_Metallic.png"),
            TryResolveBundledTexture(directory, $"{baseStem}_NormalGL.jpg"));
        return true;
    }

    private static string? TryResolveBundledTexture(string directory, string fileName)
    {
        string logicalPath = $"{directory}/{fileName}";
        return BundledDefaultMaterialAssetStore.TryGetAbsolutePath(logicalPath, out string absolutePath)
            ? absolutePath
            : null;
    }

    public static Field_colorX CreateColorMember(ResoniteColor color)
    {
        return new Field_colorX
        {
            Value = new colorX
            {
                r = (float)color.R,
                g = (float)color.G,
                b = (float)color.B,
                a = (float)color.A,
                Profile = "sRGB",
            },
        };
    }
}

internal sealed record BundledDefaultMaterialTextureSet(
    string? EmissionPath,
    string? HeightPath,
    string? MetallicPath,
    string? NormalPath);
