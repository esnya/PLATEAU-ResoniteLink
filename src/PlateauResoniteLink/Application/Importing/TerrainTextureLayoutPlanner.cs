using System;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class TerrainTextureLayoutPlanner
{
    private const double PixelEpsilon = 1e-6;

    public static TerrainTextureLayoutPlan Create(TerrainTextureOverlay terrainTextureOverlay)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureOverlay);

        return Create(terrainTextureOverlay.GeographicBounds, terrainTextureOverlay.ZoomLevel);
    }

    public static TerrainTextureLayoutPlan Create(
        GeographicRectangle bounds,
        int zoomLevel)
    {
        double leftPixel = WebMercatorTileMath.LongitudeToPixelX(bounds.MinLongitude, zoomLevel);
        double rightPixel = WebMercatorTileMath.LongitudeToPixelX(bounds.MaxLongitude, zoomLevel);
        double topPixel = WebMercatorTileMath.LatitudeToPixelY(bounds.MaxLatitude, zoomLevel);
        double bottomPixel = WebMercatorTileMath.LatitudeToPixelY(bounds.MinLatitude, zoomLevel);

        if (rightPixel - leftPixel <= PixelEpsilon || bottomPixel - topPixel <= PixelEpsilon)
        {
            throw new InvalidOperationException(
                "Terrain texture overlay has degenerate geographic bounds.");
        }

        int minTileX = (int)Math.Floor(leftPixel / WebMercatorTileMath.TileSizePixels);
        int maxTileX = (int)Math.Floor((rightPixel - PixelEpsilon) / WebMercatorTileMath.TileSizePixels);
        int minTileY = (int)Math.Floor(topPixel / WebMercatorTileMath.TileSizePixels);
        int maxTileY = (int)Math.Floor((bottomPixel - PixelEpsilon) / WebMercatorTileMath.TileSizePixels);
        int stitchedWidth = (maxTileX - minTileX + 1) * WebMercatorTileMath.TileSizePixels;
        int stitchedHeight = (maxTileY - minTileY + 1) * WebMercatorTileMath.TileSizePixels;
        int cropLeft = Math.Clamp(
            (int)Math.Floor(leftPixel - (minTileX * WebMercatorTileMath.TileSizePixels)),
            0,
            stitchedWidth - 1);
        int cropTop = Math.Clamp(
            (int)Math.Floor(topPixel - (minTileY * WebMercatorTileMath.TileSizePixels)),
            0,
            stitchedHeight - 1);
        int cropRight = Math.Clamp(
            (int)Math.Ceiling((rightPixel - (minTileX * WebMercatorTileMath.TileSizePixels)) - PixelEpsilon),
            cropLeft + 1,
            stitchedWidth);
        int cropBottom = Math.Clamp(
            (int)Math.Ceiling((bottomPixel - (minTileY * WebMercatorTileMath.TileSizePixels)) - PixelEpsilon),
            cropTop + 1,
            stitchedHeight);

        return new TerrainTextureLayoutPlan(
            minTileX,
            maxTileX,
            minTileY,
            maxTileY,
            stitchedWidth,
            stitchedHeight,
            cropLeft,
            cropTop,
            cropRight - cropLeft,
            cropBottom - cropTop);
    }
}

internal sealed record TerrainTextureLayoutPlan(
    int MinTileX,
    int MaxTileX,
    int MinTileY,
    int MaxTileY,
    int StitchedWidth,
    int StitchedHeight,
    int CropLeft,
    int CropTop,
    int CropWidth,
    int CropHeight);
