using System;

namespace PlateauResoniteLink.Targets.Resonite;

public static class ResoniteMaterialSharing
{
    private static readonly ResoniteColor SharedBaseColor = new(1.0, 1.0, 1.0, 1.0);

    public static bool CanUseSharedAlbedoOnlyMaterial(ResoniteMaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(material);

        return material.MaterialType == ResoniteMaterialType.Standard
            && material.Projection == ResoniteMaterialProjection.Uv
            && material.TextureSourceKind == ResoniteTextureSourceKind.Dataset
            && material.TexturePayload is not null
            && material.DepthOffset is null
            && material.TextureScale is null
            && material.TextureOffset is null
            && material.BaseColor == SharedBaseColor
            && !ResoniteMaterialComponentPolicy.HasRepresentableOpticalProperties(material);
    }

    public static bool CanUseSharedVertexColorMaterial(ResoniteMaterialBinding material)
    {
        ArgumentNullException.ThrowIfNull(material);

        return material.MaterialType == ResoniteMaterialType.VertexColor
            && material.Projection == ResoniteMaterialProjection.Uv
            && IsWhiteBaseColor(material.BaseColor)
            && material.TexturePayload is null
            && material.TerrainOverlay is null
            && material.TextureScale is null
            && material.TextureOffset is null
            && !ResoniteMaterialComponentPolicy.HasRepresentableOpticalProperties(material);
    }


    public static string CreateCanonicalGenericSharedMaterialKey(
        ResoniteMaterialProjection projection,
        ResoniteFloat2? textureScale,
        ResoniteFloat2? textureOffset,
        ResoniteMaterialDepthOffset? depthOffset)
    {
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"shared-generic-{ProjectionToken(projection)}-scale-{FormatFloat2(textureScale)}-offset-{FormatFloat2(textureOffset)}-depth-{FormatDepth(depthOffset)}");
    }

    public static string CreateCanonicalVertexColorCommonMaterialKey(
        ResoniteMaterialProjection projection,
        ResoniteMaterialDepthOffset? depthOffset)
    {
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"shared-vertex-{ProjectionToken(projection)}-depth-{FormatDepth(depthOffset)}");
    }

    public static bool IsWhiteBaseColor(ResoniteColor color)
    {
        return Math.Abs(color.R - 1.0) < 1e-9
            && Math.Abs(color.G - 1.0) < 1e-9
            && Math.Abs(color.B - 1.0) < 1e-9
            && Math.Abs(color.A - 1.0) < 1e-9;
    }

    private static string FormatFloat2(ResoniteFloat2? value)
    {
        return value is null
            ? "none"
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{FormatRounded(value.X)}-{FormatRounded(value.Y)}");
    }

    private static string FormatDepth(ResoniteMaterialDepthOffset? value)
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

    private static string ProjectionToken(ResoniteMaterialProjection projection)
    {
        return projection switch
        {
            ResoniteMaterialProjection.Uv => "uv",
            ResoniteMaterialProjection.Triplanar => "triplanar",
            _ => projection.ToString().ToLowerInvariant(),
        };
    }
}
