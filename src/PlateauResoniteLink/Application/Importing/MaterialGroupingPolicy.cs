using System;
using System.Globalization;

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
            if (TerrainOverlayMeshCodeResolver.ResolveMeshCode(actualMeshCode, material.TerrainOverlay) is null)
            {
                throw CreateTerrainOverlayMeshCodeMismatchException(
                    actualMeshCode,
                    material.TerrainOverlay);
            }

            return new MaterialGroupingKey(
                material.MaterialType,
                TexturePayloadIdentity: null,
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
            material.TexturePayload?.Identity,
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

    private static InvalidOperationException CreateTerrainOverlayMeshCodeMismatchException(
        string actualMeshCode,
        TerrainTextureOverlay terrainOverlay)
    {
        string overlaySummary = string.Create(
            CultureInfo.InvariantCulture,
            $"package='{terrainOverlay.PackageName}', bounds='{FormatBounds(terrainOverlay.GeographicBounds)}', sources='{terrainOverlay.SourceDescriptorKey}'");

        return new InvalidOperationException(
            $"Terrain overlay material requires a third-level mesh-code that matches the overlay geographic bounds. "
            + $"phase='material-grouping', actual_mesh_code='{actualMeshCode}', requested_mesh_code='{actualMeshCode}', "
            + $"requested_mesh_code_bounds='<none>', overlay={overlaySummary}.");
    }

    private static string FormatBounds(GeographicRectangle bounds) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{FormatRounded(bounds.MinLatitude)}-{FormatRounded(bounds.MaxLatitude)}-{FormatRounded(bounds.MinLongitude)}-{FormatRounded(bounds.MaxLongitude)}");

    private static string FormatRounded(double value)
    {
        double rounded = Math.Round(value, 6, MidpointRounding.AwayFromZero);
        return (rounded == 0.0 ? 0.0 : rounded).ToString("0.######", CultureInfo.InvariantCulture);
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
    string? TexturePayloadIdentity,
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
