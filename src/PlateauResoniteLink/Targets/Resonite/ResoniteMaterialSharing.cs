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
            && material.BaseColor == SharedBaseColor;
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
            && material.TextureOffset is null;
    }


    public static string CreateCanonicalGenericSharedMaterialKey(
        ResoniteMaterialProjection projection,
        ResoniteFloat2? textureScale,
        ResoniteFloat2? textureOffset,
        ResoniteMaterialDepthOffset? depthOffset)
    {
        _ = textureScale;
        _ = textureOffset;

        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"generic/{ProjectionToken(projection)}{TerrainAlignedSuffix(depthOffset)}");
    }

    public static string CreateCanonicalVertexColorCommonMaterialKey(
        ResoniteMaterialProjection projection,
        ResoniteMaterialDepthOffset? depthOffset)
    {
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"vertex-color/{ProjectionToken(projection)}{TerrainAlignedSuffix(depthOffset)}");
    }

    public static bool IsWhiteBaseColor(ResoniteColor color)
    {
        return Math.Abs(color.R - 1.0) < 1e-9
            && Math.Abs(color.G - 1.0) < 1e-9
            && Math.Abs(color.B - 1.0) < 1e-9
            && Math.Abs(color.A - 1.0) < 1e-9;
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

    private static string TerrainAlignedSuffix(ResoniteMaterialDepthOffset? depthOffset) =>
        depthOffset is null ? string.Empty : "-terrain-aligned";
}
