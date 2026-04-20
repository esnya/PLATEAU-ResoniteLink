using System.Globalization;

namespace PlateauResoniteLink.Domain.Importing;

public static class WebMercatorTileMath
{
    public const int TileSizePixels = 256;
    public const double MaxLatitude = 85.05112878;
    private const double DegreesToRadians = Math.PI / 180.0;
    private const double RadiansToDegrees = 180.0 / Math.PI;

    public static double LongitudeToNormalizedX(double longitude)
    {
        return (longitude + 180.0) / 360.0;
    }

    public static double LatitudeToNormalizedY(double latitude)
    {
        double clampedLatitude = Math.Clamp(latitude, -MaxLatitude, MaxLatitude);
        double sine = Math.Sin(clampedLatitude * DegreesToRadians);
        return 0.5 - (Math.Log((1.0 + sine) / (1.0 - sine)) / (4.0 * Math.PI));
    }

    public static double LongitudeToPixelX(double longitude, int zoomLevel)
    {
        return LongitudeToNormalizedX(longitude) * GetWorldSizePixels(zoomLevel);
    }

    public static double LatitudeToPixelY(double latitude, int zoomLevel)
    {
        return LatitudeToNormalizedY(latitude) * GetWorldSizePixels(zoomLevel);
    }

    public static double NormalizedXToLongitude(double normalizedX)
    {
        return (normalizedX * 360.0) - 180.0;
    }

    public static double NormalizedYToLatitude(double normalizedY)
    {
        double mercator = Math.PI * (1.0 - (2.0 * normalizedY));
        return Math.Atan(Math.Sinh(mercator)) * RadiansToDegrees;
    }

    public static double PixelXToLongitude(double pixelX, int zoomLevel)
    {
        return NormalizedXToLongitude(pixelX / GetWorldSizePixels(zoomLevel));
    }

    public static double PixelYToLatitude(double pixelY, int zoomLevel)
    {
        return NormalizedYToLatitude(pixelY / GetWorldSizePixels(zoomLevel));
    }

    public static string FormatTileUrl(string urlTemplate, int zoomLevel, int tileX, int tileY)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(urlTemplate);

        return urlTemplate
            .Replace("{z}", zoomLevel.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{x}", tileX.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{y}", tileY.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static double GetWorldSizePixels(int zoomLevel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(zoomLevel);

        return TileSizePixels * Math.Pow(2.0, zoomLevel);
    }
}
