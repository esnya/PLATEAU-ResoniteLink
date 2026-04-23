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
        return StableOpaqueId.Create(
            "shared-generic",
            builder =>
            {
                builder.Add(ProjectionToken(projection));
                AddFloat2(builder, textureScale);
                AddFloat2(builder, textureOffset);
                AddDepth(builder, depthOffset);
            });
    }

    public static string CreateCanonicalVertexColorCommonMaterialKey(
        ResoniteMaterialProjection projection,
        ResoniteMaterialDepthOffset? depthOffset)
    {
        return StableOpaqueId.Create(
            "shared-vertex",
            builder =>
            {
                builder.Add(ProjectionToken(projection));
                AddDepth(builder, depthOffset);
            });
    }

    public static bool IsWhiteBaseColor(ResoniteColor color)
    {
        return Math.Abs(color.R - 1.0) < 1e-9
            && Math.Abs(color.G - 1.0) < 1e-9
            && Math.Abs(color.B - 1.0) < 1e-9
            && Math.Abs(color.A - 1.0) < 1e-9;
    }

    private static void AddFloat2(StableOpaqueId.Builder builder, ResoniteFloat2? value)
    {
        builder.AddRounded(value?.X);
        builder.AddRounded(value?.Y);
    }

    private static void AddDepth(StableOpaqueId.Builder builder, ResoniteMaterialDepthOffset? value)
    {
        builder.AddRounded(value?.Factor);
        builder.AddRounded(value?.Units);
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
