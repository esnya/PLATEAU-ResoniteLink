using System;

using PlateauResoniteLink.Domain.Importing;

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
        string scaleToken = CreateFloat2Token(textureScale);
        string offsetToken = CreateFloat2Token(textureOffset);
        string depthToken = CreateDepthToken(depthOffset);
        return $"generic|{projection}|scale:{scaleToken}|offset:{offsetToken}|depth:{depthToken}";
    }

    public static string CreateCanonicalVertexColorCommonMaterialKey(
        ResoniteMaterialProjection projection,
        ResoniteMaterialDepthOffset? depthOffset)
    {
        return $"vertex-color|{projection}|depth:{CreateDepthToken(depthOffset)}";
    }

    public static bool IsWhiteBaseColor(ResoniteColor color)
    {
        return Math.Abs(color.R - 1.0) < 1e-9
            && Math.Abs(color.G - 1.0) < 1e-9
            && Math.Abs(color.B - 1.0) < 1e-9
            && Math.Abs(color.A - 1.0) < 1e-9;
    }

    private static string CreateFloat2Token(ResoniteFloat2? value)
    {
        return value is null
            ? "none"
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{value.X:0.######}x{value.Y:0.######}");
    }

    private static string CreateDepthToken(ResoniteMaterialDepthOffset? value)
    {
        return value is null
            ? "none"
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{value.Factor:0.######}x{value.Units:0.######}");
    }
}
