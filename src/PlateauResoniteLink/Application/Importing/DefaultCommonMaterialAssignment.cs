using System;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class DefaultCommonMaterialAssignment
{
    public static DefaultCommonMaterialMember? Resolve(
        ColorRgba baseColor,
        MaterialType materialType,
        TexturePayload? texturePayload,
        TextureSourceKind textureSourceKind,
        MaterialProjection projection,
        MaterialDepthOffset? depthOffset,
        Float2? textureScale,
        Float2? textureOffset,
        TerrainTextureOverlay? terrainOverlay,
        DefaultCommonMaterialMember? existingCommonMaterial)
    {
        if (!IsCanonicalCommonBaseColor(baseColor)
            || projection != MaterialProjection.Uv && existingCommonMaterial?.Kind is not DefaultCommonMaterialMemberKind.Bundled)
        {
            return null;
        }

        if (existingCommonMaterial is not null)
        {
            return ResolveExistingCommonMaterial(
                existingCommonMaterial,
                materialType,
                texturePayload,
                textureSourceKind,
                projection,
                depthOffset,
                textureScale,
                textureOffset,
                terrainOverlay);
        }

        if (IsDatasetGenericUvMaterial(
                materialType,
                texturePayload,
                textureSourceKind,
                projection,
                textureScale,
                textureOffset))
        {
            return DefaultCommonMaterialMember.GenericUv(depthOffset);
        }

        if (materialType == MaterialType.VertexColor
            && projection == MaterialProjection.Uv
            && texturePayload is null
            && textureScale is null
            && textureOffset is null)
        {
            return DefaultCommonMaterialMember.VertexColorUv(depthOffset);
        }

        return null;
    }

    private static DefaultCommonMaterialMember? ResolveExistingCommonMaterial(
        DefaultCommonMaterialMember existingCommonMaterial,
        MaterialType materialType,
        TexturePayload? texturePayload,
        TextureSourceKind textureSourceKind,
        MaterialProjection projection,
        MaterialDepthOffset? depthOffset,
        Float2? textureScale,
        Float2? textureOffset,
        TerrainTextureOverlay? terrainOverlay)
    {
        return existingCommonMaterial.Kind switch
        {
            DefaultCommonMaterialMemberKind.Bundled => depthOffset is null && texturePayload is null && terrainOverlay is null
                ? existingCommonMaterial
                : null,
            DefaultCommonMaterialMemberKind.GenericAlbedo => IsDatasetGenericUvMaterial(
                    materialType,
                    texturePayload,
                    textureSourceKind,
                    projection,
                    textureScale,
                    textureOffset)
                ? DefaultCommonMaterialMember.GenericUv(depthOffset)
                : null,
            DefaultCommonMaterialMemberKind.VertexColor => materialType == MaterialType.VertexColor
                && projection == MaterialProjection.Uv
                && texturePayload is null
                && textureScale is null
                && textureOffset is null
                    ? DefaultCommonMaterialMember.VertexColorUv(depthOffset)
                    : null,
            _ => throw new InvalidOperationException($"Unsupported common material member kind '{existingCommonMaterial.Kind}'."),
        };
    }

    private static bool IsDatasetGenericUvMaterial(
        MaterialType materialType,
        TexturePayload? texturePayload,
        TextureSourceKind textureSourceKind,
        MaterialProjection projection,
        Float2? textureScale,
        Float2? textureOffset)
    {
        return materialType == MaterialType.Standard
            && projection == MaterialProjection.Uv
            && textureSourceKind == TextureSourceKind.Dataset
            && (texturePayload is not null || textureScale is null && textureOffset is null);
    }

    private static bool IsCanonicalCommonBaseColor(ColorRgba color)
    {
        return Math.Abs(color.R - 1.0) < 1e-9
            && Math.Abs(color.G - 1.0) < 1e-9
            && Math.Abs(color.B - 1.0) < 1e-9
            && Math.Abs(color.A - 1.0) < 1e-9;
    }
}
