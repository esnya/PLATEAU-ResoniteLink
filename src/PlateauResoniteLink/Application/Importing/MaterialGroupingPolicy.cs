using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

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
                null,
                material.TextureSourceKind,
                material.Projection,
                depthOffset,
                IsIdentityTextureScale(textureScale) ? null : textureScale,
                null,
                null,
                IsZeroTextureOffset(textureOffset) ? null : textureOffset,
                MaterialReuseScope.PerObject,
                material.BundledVariantIndex,
                null);
        }

        return new MaterialGroupingKey(
            material.MaterialType,
            material.TexturePayload,
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

internal sealed class MaterialGroupingKey : IEquatable<MaterialGroupingKey>
{
    public MaterialGroupingKey(
        MaterialType materialType,
        TexturePayload? texturePayload,
        TextureSourceKind textureSourceKind,
        MaterialProjection projection,
        MaterialDepthOffset? depthOffset,
        Float2? textureScale,
        string? family,
        ColorRgba? baseColor,
        Float2? textureOffset,
        MaterialReuseScope reuseScope,
        int? bundledVariantIndex,
        TerrainTextureOverlay? terrainOverlay)
    {
        MaterialType = materialType;
        TexturePayload = texturePayload;
        TextureSourceKind = textureSourceKind;
        Projection = projection;
        DepthOffset = depthOffset;
        TextureScale = textureScale;
        Family = family;
        BaseColor = baseColor;
        TextureOffset = textureOffset;
        ReuseScope = reuseScope;
        BundledVariantIndex = bundledVariantIndex;
        TerrainOverlay = terrainOverlay;
    }

    public MaterialType MaterialType { get; }

    public TexturePayload? TexturePayload { get; }

    public TextureSourceKind TextureSourceKind { get; }

    public MaterialProjection Projection { get; }

    public MaterialDepthOffset? DepthOffset { get; }

    public Float2? TextureScale { get; }

    public string? Family { get; }

    public ColorRgba? BaseColor { get; }

    public Float2? TextureOffset { get; }

    public MaterialReuseScope ReuseScope { get; }

    public int? BundledVariantIndex { get; }

    public TerrainTextureOverlay? TerrainOverlay { get; }

    public bool Equals(MaterialGroupingKey? other)
    {
        return other is not null
            && MaterialType == other.MaterialType
            && ReferenceEquals(TexturePayload, other.TexturePayload)
            && TextureSourceKind == other.TextureSourceKind
            && Projection == other.Projection
            && EqualityComparer<MaterialDepthOffset?>.Default.Equals(DepthOffset, other.DepthOffset)
            && EqualityComparer<Float2?>.Default.Equals(TextureScale, other.TextureScale)
            && string.Equals(Family, other.Family, StringComparison.Ordinal)
            && EqualityComparer<ColorRgba?>.Default.Equals(BaseColor, other.BaseColor)
            && EqualityComparer<Float2?>.Default.Equals(TextureOffset, other.TextureOffset)
            && ReuseScope == other.ReuseScope
            && BundledVariantIndex == other.BundledVariantIndex
            && EqualityComparer<TerrainTextureOverlay?>.Default.Equals(TerrainOverlay, other.TerrainOverlay);
    }

    public override bool Equals(object? obj) => Equals(obj as MaterialGroupingKey);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(MaterialType);
        if (TexturePayload is not null)
        {
            hash.Add(RuntimeHelpers.GetHashCode(TexturePayload));
        }

        hash.Add(TextureSourceKind);
        hash.Add(Projection);
        hash.Add(DepthOffset);
        hash.Add(TextureScale);
        hash.Add(Family, StringComparer.Ordinal);
        hash.Add(BaseColor);
        hash.Add(TextureOffset);
        hash.Add(ReuseScope);
        hash.Add(BundledVariantIndex);
        hash.Add(TerrainOverlay);
        return hash.ToHashCode();
    }
}
