using GeographicLib;
using GeographicLib.Projections;

using Plateau.ResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Plateau.ResoniteLink.Targets.Resonite;

internal static class TerrainTextureGeoReferencedRasterMetadataReader
{
    private const int GeographicTypeGeoKey = 2048;
    private const int ProjectedCSTypeGeoKey = 3072;
    public static async Task<GeoReferencedRasterMetadata?> TryReadMetadataAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return null;
        }

        ImageInfo imageInfo;
        try
        {
            imageInfo = await Image.IdentifyAsync(sourcePath, cancellationToken)
                ?? throw new InvalidOperationException($"Failed to identify raster '{sourcePath}'.");
        }
        catch (UnknownImageFormatException)
        {
            return null;
        }

        ExifProfile? exifProfile = imageInfo.Metadata.ExifProfile;
        if (exifProfile is null)
        {
            return null;
        }

        double[]? tiePoints = TryGetDoubleArray(exifProfile, ExifTag.ModelTiePoint);
        double[]? pixelScale = TryGetDoubleArray(exifProfile, ExifTag.PixelScale);
        double[]? modelTransform = TryGetDoubleArray(exifProfile, ExifTag.ModelTransform);
        ushort[]? geoKeyDirectory = TryGetUnsignedShortArray(exifProfile, "GeoKeyDirectoryTag");
        double[]? geoDoubleParams = TryGetNamedDoubleArray(exifProfile, "GeoDoubleParamsTag");
        string? geoAsciiParams = TryGetNamedString(exifProfile, "GeoAsciiParamsTag");

        return TryCreateMetadata(
            imageInfo.Width,
            imageInfo.Height,
            tiePoints,
            pixelScale,
            modelTransform,
            geoKeyDirectory,
            geoDoubleParams,
            geoAsciiParams);
    }

    internal static GeoReferencedRasterMetadata? TryCreateMetadata(
        int pixelWidth,
        int pixelHeight,
        double[]? modelTiePoint,
        double[]? pixelScale,
        double[]? modelTransform,
        ushort[]? geoKeyDirectory,
        double[]? geoDoubleParams,
        string? geoAsciiParams)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return null;
        }

        if (!TryCreateModelBounds(
                pixelWidth,
                pixelHeight,
                modelTiePoint,
                pixelScale,
                modelTransform,
                out ModelSpaceRectangle modelBounds,
                out double pixelWidthInModelUnits,
                out double pixelHeightInModelUnits))
        {
            return null;
        }

        string? coordinateSystemIdentifier = TryResolveCoordinateSystemIdentifier(
            geoKeyDirectory,
            geoDoubleParams,
            geoAsciiParams);
        GeographicRectangle? geographicBounds = TryConvertToGeographicBounds(
            coordinateSystemIdentifier,
            modelBounds);
        if (geographicBounds is null)
        {
            return new GeoReferencedRasterMetadata(
                modelBounds.ToFallbackGeographicRectangle(),
                coordinateSystemIdentifier,
                PixelWidthMeters: 0.0,
                PixelHeightMeters: 0.0);
        }

        return new GeoReferencedRasterMetadata(
            geographicBounds,
            coordinateSystemIdentifier,
            GetPixelWidthMeters(coordinateSystemIdentifier, geographicBounds, pixelWidthInModelUnits),
            GetPixelHeightMeters(coordinateSystemIdentifier, geographicBounds, pixelHeightInModelUnits));
    }

    private static bool TryCreateModelBounds(
        int pixelWidth,
        int pixelHeight,
        double[]? modelTiePoint,
        double[]? pixelScale,
        double[]? modelTransform,
        out ModelSpaceRectangle bounds,
        out double pixelWidthInModelUnits,
        out double pixelHeightInModelUnits)
    {
        if (TryCreateBoundsFromTiePointAndScale(
                pixelWidth,
                pixelHeight,
                modelTiePoint,
                pixelScale,
                out bounds,
                out pixelWidthInModelUnits,
                out pixelHeightInModelUnits))
        {
            return true;
        }

        return TryCreateBoundsFromTransform(
            pixelWidth,
            pixelHeight,
            modelTransform,
            out bounds,
            out pixelWidthInModelUnits,
            out pixelHeightInModelUnits);
    }

    private static bool TryCreateBoundsFromTiePointAndScale(
        int pixelWidth,
        int pixelHeight,
        double[]? modelTiePoint,
        double[]? pixelScale,
        out ModelSpaceRectangle bounds,
        out double pixelWidthInModelUnits,
        out double pixelHeightInModelUnits)
    {
        bounds = default;
        pixelWidthInModelUnits = 0.0;
        pixelHeightInModelUnits = 0.0;
        if (modelTiePoint is null || pixelScale is null || modelTiePoint.Length < 6 || pixelScale.Length < 2)
        {
            return false;
        }

        double rasterX = modelTiePoint[0];
        double rasterY = modelTiePoint[1];
        double modelX = modelTiePoint[3];
        double modelY = modelTiePoint[4];
        double scaleX = Math.Abs(pixelScale[0]);
        double scaleY = Math.Abs(pixelScale[1]);
        if (scaleX <= 0.0 || scaleY <= 0.0)
        {
            return false;
        }

        double west = modelX - (rasterX * scaleX);
        double north = modelY + (rasterY * scaleY);
        double east = west + (pixelWidth * scaleX);
        double south = north - (pixelHeight * scaleY);
        bounds = new ModelSpaceRectangle(west, south, east, north);
        pixelWidthInModelUnits = scaleX;
        pixelHeightInModelUnits = scaleY;
        return true;
    }

    private static bool TryCreateBoundsFromTransform(
        int pixelWidth,
        int pixelHeight,
        double[]? modelTransform,
        out ModelSpaceRectangle bounds,
        out double pixelWidthInModelUnits,
        out double pixelHeightInModelUnits)
    {
        bounds = default;
        pixelWidthInModelUnits = 0.0;
        pixelHeightInModelUnits = 0.0;
        if (modelTransform is null || modelTransform.Length < 16)
        {
            return false;
        }

        double scaleX = modelTransform[0];
        double shearX = modelTransform[1];
        double shearY = modelTransform[4];
        double scaleY = modelTransform[5];
        if (Math.Abs(shearX) > 1e-9 || Math.Abs(shearY) > 1e-9 || scaleX <= 0.0 || scaleY >= 0.0)
        {
            return false;
        }

        double translateX = modelTransform[3];
        double translateY = modelTransform[7];
        double west = translateX;
        double north = translateY;
        double east = west + (pixelWidth * scaleX);
        double south = north + (pixelHeight * scaleY);
        bounds = new ModelSpaceRectangle(west, south, east, north);
        pixelWidthInModelUnits = Math.Abs(scaleX);
        pixelHeightInModelUnits = Math.Abs(scaleY);
        return true;
    }

    private static string? TryResolveCoordinateSystemIdentifier(
        ushort[]? geoKeyDirectory,
        double[]? geoDoubleParams,
        string? geoAsciiParams)
    {
        if (geoKeyDirectory is null || geoKeyDirectory.Length < 8)
        {
            return null;
        }

        _ = geoDoubleParams;
        _ = geoAsciiParams;

        int keyCount = geoKeyDirectory[3];
        for (int index = 0; index < keyCount; index++)
        {
            int entryOffset = 4 + (index * 4);
            if (entryOffset + 3 >= geoKeyDirectory.Length)
            {
                break;
            }

            int keyId = geoKeyDirectory[entryOffset];
            int tiffTagLocation = geoKeyDirectory[entryOffset + 1];
            int valueCount = geoKeyDirectory[entryOffset + 2];
            int valueOffset = geoKeyDirectory[entryOffset + 3];
            if (valueCount != 1 || tiffTagLocation != 0)
            {
                continue;
            }

            if (keyId == GeographicTypeGeoKey || keyId == ProjectedCSTypeGeoKey)
            {
                return $"EPSG:{valueOffset}";
            }
        }

        return null;
    }

    private static GeographicRectangle? TryConvertToGeographicBounds(
        string? coordinateSystemIdentifier,
        ModelSpaceRectangle modelBounds)
    {
        if (string.IsNullOrWhiteSpace(coordinateSystemIdentifier))
        {
            return null;
        }

        if (string.Equals(coordinateSystemIdentifier, "EPSG:4326", StringComparison.OrdinalIgnoreCase)
            || string.Equals(coordinateSystemIdentifier, "EPSG:6668", StringComparison.OrdinalIgnoreCase))
        {
            return new GeographicRectangle(
                modelBounds.MinY,
                modelBounds.MaxY,
                modelBounds.MinX,
                modelBounds.MaxX);
        }

        if (!TryParseJapanPlaneRectangularZone(coordinateSystemIdentifier, out int zone))
        {
            return null;
        }

        (double originLatitude, double originLongitude) = JapanPlaneRectangularZoneOrigins[zone - 1];
        TransverseMercator projection = new(Ellipsoid.GRS80, JapanPlaneRectangularCentralScale);
        (_, double originNorthing) = projection.Forward(originLongitude, originLatitude, originLongitude);

        (double minLatitude, double minLongitude) = ReverseProjected(projection, originLongitude, originNorthing, modelBounds.MinX, modelBounds.MinY);
        (double maxLatitude, double maxLongitude) = ReverseProjected(projection, originLongitude, originNorthing, modelBounds.MaxX, modelBounds.MaxY);
        return new GeographicRectangle(
            MinLatitude: Math.Min(minLatitude, maxLatitude),
            MaxLatitude: Math.Max(minLatitude, maxLatitude),
            MinLongitude: Math.Min(minLongitude, maxLongitude),
            MaxLongitude: Math.Max(minLongitude, maxLongitude));
    }

    private static double GetPixelWidthMeters(
        string? coordinateSystemIdentifier,
        GeographicRectangle geographicBounds,
        double pixelWidthInModelUnits)
    {
        if (pixelWidthInModelUnits <= 0.0)
        {
            return 0.0;
        }

        return IsGeographicCoordinateSystem(coordinateSystemIdentifier)
            ? DegreesLongitudeToMeters((geographicBounds.MinLatitude + geographicBounds.MaxLatitude) * 0.5, pixelWidthInModelUnits)
            : pixelWidthInModelUnits;
    }

    private static double GetPixelHeightMeters(
        string? coordinateSystemIdentifier,
        GeographicRectangle geographicBounds,
        double pixelHeightInModelUnits)
    {
        if (pixelHeightInModelUnits <= 0.0)
        {
            return 0.0;
        }

        return IsGeographicCoordinateSystem(coordinateSystemIdentifier)
            ? DegreesLatitudeToMeters(pixelHeightInModelUnits)
            : pixelHeightInModelUnits;
    }

    private static bool IsGeographicCoordinateSystem(string? coordinateSystemIdentifier)
    {
        return string.Equals(coordinateSystemIdentifier, "EPSG:4326", StringComparison.OrdinalIgnoreCase)
            || string.Equals(coordinateSystemIdentifier, "EPSG:6668", StringComparison.OrdinalIgnoreCase);
    }

    private static (double Latitude, double Longitude) ReverseProjected(
        TransverseMercator projection,
        double centralMeridian,
        double originNorthing,
        double x,
        double y)
    {
        (double latitude, double longitude) = projection.Reverse(centralMeridian, x, y + originNorthing);
        return (latitude, longitude);
    }

    private static bool TryParseJapanPlaneRectangularZone(
        string coordinateSystemIdentifier,
        out int zone)
    {
        zone = 0;
        if (!coordinateSystemIdentifier.StartsWith("EPSG:", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(coordinateSystemIdentifier["EPSG:".Length..], out int epsgCode))
        {
            return false;
        }

        zone = epsgCode - 6668;
        return zone is >= 1 and <= 19;
    }

    private static double DegreesLatitudeToMeters(double degrees)
    {
        return Math.Abs(degrees) * 111_320.0;
    }

    private static double DegreesLongitudeToMeters(double latitude, double degrees)
    {
        return Math.Abs(degrees) * 111_320.0 * Math.Cos(latitude * (Math.PI / 180.0));
    }

    private static double[]? TryGetDoubleArray(ExifProfile exifProfile, ExifTag<double[]> tag)
    {
        return exifProfile.TryGetValue(tag, out IExifValue<double[]>? exifValue)
            ? exifValue.Value
            : null;
    }

    private static ushort[]? TryGetUnsignedShortArray(ExifProfile exifProfile, string tagName)
    {
        object? value = TryGetNamedValue(exifProfile, tagName);
        return value switch
        {
            ushort[] ushortArray => ushortArray,
            short[] shortArray => shortArray.Select(static item => unchecked((ushort)item)).ToArray(),
            _ => null,
        };
    }

    private static double[]? TryGetNamedDoubleArray(ExifProfile exifProfile, string tagName)
    {
        return TryGetNamedValue(exifProfile, tagName) as double[];
    }

    private static string? TryGetNamedString(ExifProfile exifProfile, string tagName)
    {
        return TryGetNamedValue(exifProfile, tagName) as string;
    }

    private static object? TryGetNamedValue(ExifProfile exifProfile, string tagName)
    {
        foreach (IExifValue value in exifProfile.Values)
        {
            if (!string.Equals(value.Tag.ToString(), tagName, StringComparison.Ordinal))
            {
                continue;
            }

            return value.GetType().GetProperty("Value")?.GetValue(value);
        }

        return null;
    }

    private const double JapanPlaneRectangularCentralScale = 0.9999;

    private static readonly (double Latitude, double Longitude)[] JapanPlaneRectangularZoneOrigins =
    [
        (33.0, 129.5),
        (33.0, 131.0),
        (36.0, 132.16666666666666),
        (33.0, 133.5),
        (36.0, 134.33333333333334),
        (36.0, 136.0),
        (36.0, 137.16666666666666),
        (36.0, 138.5),
        (36.0, 139.83333333333334),
        (40.0, 140.83333333333334),
        (44.0, 140.25),
        (44.0, 142.25),
        (44.0, 144.25),
        (26.0, 142.0),
        (26.0, 127.5),
        (26.0, 124.0),
        (26.0, 131.0),
        (20.0, 136.0),
        (26.0, 154.0),
    ];

    private readonly record struct ModelSpaceRectangle(
        double MinX,
        double MinY,
        double MaxX,
        double MaxY)
    {
        public GeographicRectangle ToFallbackGeographicRectangle()
        {
            return new GeographicRectangle(MinY, MaxY, MinX, MaxX);
        }
    }
}

internal static class TerrainTextureGeoReferencedRasterCropper
{
    public static Image<Rgba32>? TryCrop(
        Image<Rgba32> sourceImage,
        GeoReferencedRasterMetadata metadata,
        GeographicRectangle requestedBounds)
    {
        if (!metadata.IsUsable)
        {
            return null;
        }

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
        double v0 = (rasterBounds.MaxLatitude - intersection.MaxLatitude) / (rasterBounds.MaxLatitude - rasterBounds.MinLatitude);
        double v1 = (rasterBounds.MaxLatitude - intersection.MinLatitude) / (rasterBounds.MaxLatitude - rasterBounds.MinLatitude);

        int left = Math.Clamp((int)Math.Floor(u0 * sourceImage.Width), 0, sourceImage.Width - 1);
        int top = Math.Clamp((int)Math.Floor(v0 * sourceImage.Height), 0, sourceImage.Height - 1);
        int right = Math.Clamp((int)Math.Ceiling(u1 * sourceImage.Width), left + 1, sourceImage.Width);
        int bottom = Math.Clamp((int)Math.Ceiling(v1 * sourceImage.Height), top + 1, sourceImage.Height);

        return sourceImage.Clone(context => context.Crop(new Rectangle(left, top, right - left, bottom - top)));
    }
}
