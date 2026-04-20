namespace Plateau.ResoniteLink.Domain.Importing;

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
            && IsIdentityTextureScale(material.TextureScale)
            && IsZeroTextureOffset(material.TextureOffset)
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
            && IsIdentityTextureScale(material.TextureScale)
            && IsZeroTextureOffset(material.TextureOffset);
    }

    public static ResoniteMaterialBinding CreateSharedAlbedoCommonMaterial()
    {
        return new ResoniteMaterialBinding(
            MaterialKey: CreateCanonicalGenericSharedMaterialKey(
                ResoniteMaterialProjection.Uv,
                textureScale: null,
                textureOffset: null,
                depthOffset: null),
            BaseColor: SharedBaseColor,
            MaterialType: ResoniteMaterialType.Standard,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Dataset,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0],
            TextureScale: null,
            Family: null,
            TextureOffset: null,
            AssetScope: ResoniteMaterialAssetScope.Common);
    }

    public static ResoniteMaterialBinding CreateSharedVertexColorCommonMaterial(
        ResoniteMaterialDepthOffset? depthOffset = null)
    {
        return new ResoniteMaterialBinding(
            MaterialKey: CreateCanonicalVertexColorCommonMaterialKey(ResoniteMaterialProjection.Uv, depthOffset),
            BaseColor: SharedBaseColor,
            MaterialType: ResoniteMaterialType.VertexColor,
            TexturePayload: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: depthOffset,
            SubmeshIndices: [0],
            TextureScale: null,
            Family: null,
            TextureOffset: null,
            AssetScope: ResoniteMaterialAssetScope.Common);
    }

    public static string CreateCanonicalGenericSharedMaterialKey(
        ResoniteMaterialProjection projection,
        ResoniteFloat2? textureScale,
        ResoniteFloat2? textureOffset,
        ResoniteMaterialDepthOffset? depthOffset)
    {
        ResoniteFloat2? normalizedTextureScale = IsIdentityTextureScale(textureScale) ? null : textureScale;
        ResoniteFloat2? normalizedTextureOffset = IsZeroTextureOffset(textureOffset) ? null : textureOffset;
        string scaleToken = CreateFloat2Token(normalizedTextureScale);
        string offsetToken = CreateFloat2Token(normalizedTextureOffset);
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

    private static bool IsIdentityTextureScale(ResoniteFloat2? textureScale)
    {
        return textureScale is null
            || (Math.Abs(textureScale.X - 1.0) < 1e-9
                && Math.Abs(textureScale.Y - 1.0) < 1e-9);
    }

    private static bool IsZeroTextureOffset(ResoniteFloat2? textureOffset)
    {
        return textureOffset is null
            || (Math.Abs(textureOffset.X) < 1e-9
                && Math.Abs(textureOffset.Y) < 1e-9);
    }
}
