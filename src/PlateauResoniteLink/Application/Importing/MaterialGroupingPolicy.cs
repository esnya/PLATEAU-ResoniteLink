using System;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class MaterialGroupingPolicy
{
    internal static MaterialGroupingKey CreateKey(
        string actualMeshCode,
        ResolvedMaterial material,
        MaterialDepthOffset? depthOffset,
        Float2? textureScale,
        ColorRgba color,
        Float2? textureOffset = null)
    {
        if (material.TerrainOverlay is not null)
        {
            return new MaterialGroupingKey(
                material.MaterialType,
                TextureSourceIdentity: null,
                material.TextureSourceKind,
                material.Projection,
                depthOffset,
                IsIdentityTextureScale(textureScale) ? null : textureScale,
                Family: null,
                BaseColor: null,
                IsZeroTextureOffset(textureOffset) ? null : textureOffset,
                MaterialReuseScope.PerObject,
                material.BundledVariantIndex,
                TerrainOverlay: null);
        }

        return new MaterialGroupingKey(
            material.MaterialType,
            material.TexturePayload?.Source.Identity,
            material.TextureSourceKind,
            material.Projection,
            depthOffset,
            textureScale,
            material.Family,
            color,
            textureOffset,
            material.ReuseScope,
            material.BundledVariantIndex,
            material.TerrainOverlay);
    }

    private static bool IsZeroTextureOffset(Float2? textureOffset)
    {
        return textureOffset is null
            || (Math.Abs(textureOffset.X) < 1e-9
                && Math.Abs(textureOffset.Y) < 1e-9);
    }

    private static bool IsIdentityTextureScale(Float2? textureScale)
    {
        return textureScale is null
            || (Math.Abs(textureScale.X - 1.0) < 1e-9
                && Math.Abs(textureScale.Y - 1.0) < 1e-9);
    }
}

internal sealed record MaterialGroupingKey(
    MaterialType MaterialType,
    TextureImportSourceIdentity? TextureSourceIdentity,
    TextureSourceKind TextureSourceKind,
    MaterialProjection Projection,
    MaterialDepthOffset? DepthOffset,
    Float2? TextureScale,
    string? Family,
    ColorRgba? BaseColor,
    Float2? TextureOffset,
    MaterialReuseScope ReuseScope,
    int? BundledVariantIndex,
    TerrainTextureOverlay? TerrainOverlay);
