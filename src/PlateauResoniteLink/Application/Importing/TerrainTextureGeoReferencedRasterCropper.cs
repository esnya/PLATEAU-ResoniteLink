using System;

using PlateauResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PlateauResoniteLink.Application.Importing;

internal static class TerrainTextureGeoReferencedRasterCropper
{
    public static Image<Rgba32>? TryCrop(
        Image<Rgba32> sourceImage,
        GeoReferencedRasterMetadata metadata,
        GeographicRectangle requestedBounds)
    {
        GeographicRectangle rasterBounds = metadata.GeographicBounds;
        GeographicRectangle intersection = new(
            Math.Max(requestedBounds.MinLatitude, rasterBounds.MinLatitude),
            Math.Min(requestedBounds.MaxLatitude, rasterBounds.MaxLatitude),
            Math.Max(requestedBounds.MinLongitude, rasterBounds.MinLongitude),
            Math.Min(requestedBounds.MaxLongitude, rasterBounds.MaxLongitude));
        if (intersection.MaxLatitude <= intersection.MinLatitude
            || intersection.MaxLongitude <= intersection.MinLongitude)
        {
            return null;
        }

        double u0 = (intersection.MinLongitude - rasterBounds.MinLongitude) / (rasterBounds.MaxLongitude - rasterBounds.MinLongitude);
        double u1 = (intersection.MaxLongitude - rasterBounds.MinLongitude) / (rasterBounds.MaxLongitude - rasterBounds.MinLongitude);
        double v0 = NormalizeVerticalPosition(metadata, rasterBounds, intersection.MaxLatitude);
        double v1 = NormalizeVerticalPosition(metadata, rasterBounds, intersection.MinLatitude);
        double requestedV0 = NormalizeVerticalPosition(metadata, rasterBounds, requestedBounds.MaxLatitude);
        double requestedV1 = NormalizeVerticalPosition(metadata, rasterBounds, requestedBounds.MinLatitude);
        double requestedU0 = (requestedBounds.MinLongitude - rasterBounds.MinLongitude) / (rasterBounds.MaxLongitude - rasterBounds.MinLongitude);
        double requestedU1 = (requestedBounds.MaxLongitude - rasterBounds.MinLongitude) / (rasterBounds.MaxLongitude - rasterBounds.MinLongitude);

        int left = Math.Clamp((int)Math.Floor(u0 * sourceImage.Width), 0, sourceImage.Width - 1);
        int top = Math.Clamp((int)Math.Floor(v0 * sourceImage.Height), 0, sourceImage.Height - 1);
        int right = Math.Clamp((int)Math.Ceiling(u1 * sourceImage.Width), left + 1, sourceImage.Width);
        int bottom = Math.Clamp((int)Math.Ceiling(v1 * sourceImage.Height), top + 1, sourceImage.Height);
        int canvasWidth = Math.Max(
            right - left,
            (int)Math.Round(sourceImage.Width * (requestedU1 - requestedU0), MidpointRounding.AwayFromZero));
        int canvasHeight = Math.Max(
            bottom - top,
            (int)Math.Round(sourceImage.Height * (requestedV1 - requestedV0), MidpointRounding.AwayFromZero));
        int offsetX = Math.Clamp(
            (int)Math.Round(sourceImage.Width * (u0 - requestedU0), MidpointRounding.AwayFromZero),
            0,
            Math.Max(0, canvasWidth - (right - left)));
        int offsetY = Math.Clamp(
            (int)Math.Round(sourceImage.Height * (v0 - requestedV0), MidpointRounding.AwayFromZero),
            0,
            Math.Max(0, canvasHeight - (bottom - top)));

        using Image<Rgba32> cropped = sourceImage.Clone(context => context.Crop(new Rectangle(left, top, right - left, bottom - top)));
        Image<Rgba32> overlayCanvas = new(canvasWidth, canvasHeight);
        overlayCanvas.Mutate(context => context.DrawImage(cropped, new SixLabors.ImageSharp.Point(offsetX, offsetY), 1.0f));
        return overlayCanvas;
    }

    private static double NormalizeVerticalPosition(
        GeoReferencedRasterMetadata metadata,
        GeographicRectangle rasterBounds,
        double latitude)
    {
        if (string.Equals(metadata.CoordinateSystemIdentifier, "EPSG:3857", StringComparison.OrdinalIgnoreCase))
        {
            double maxY = ToMercatorY(rasterBounds.MaxLatitude);
            double minY = ToMercatorY(rasterBounds.MinLatitude);
            double latitudeY = ToMercatorY(latitude);
            return (maxY - latitudeY) / (maxY - minY);
        }

        return (rasterBounds.MaxLatitude - latitude) / (rasterBounds.MaxLatitude - rasterBounds.MinLatitude);
    }

    private static double ToMercatorY(double latitude)
    {
        double radians = latitude * (Math.PI / 180.0);
        return Math.Log(Math.Tan((Math.PI / 4.0) + (radians / 2.0)));
    }
}
