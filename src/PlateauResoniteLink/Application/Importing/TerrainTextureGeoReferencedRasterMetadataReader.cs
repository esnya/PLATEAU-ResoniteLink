using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using GeographicLib;
using GeographicLib.Projections;

using PlateauResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace PlateauResoniteLink.Application.Importing;

internal static class TerrainTextureGeoReferencedRasterMetadataReader
{
    private const int GeographicTypeGeoKey = 2048;
    private const int ProjectedCSTypeGeoKey = 3072;

    public static async Task<GeoReferencedRasterMetadata?> TryReadMetadataAsync(
        ITerrainTextureRasterContentSource source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            await using Stream stream = await source.OpenReadAsync(cancellationToken);
            return await TryReadMetadataAsync(stream, cancellationToken);
        }
        catch (Exception exception) when (IsCandidateRasterReadFailure(exception))
        {
            return null;
        }
    }

    public static async Task<GeoReferencedRasterMetadata?> TryReadMetadataAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return null;
        }

        try
        {
            ImageInfo? identifiedImage = await Image.IdentifyAsync(sourcePath, cancellationToken);
            if (identifiedImage is null)
            {
                return null;
            }

            ImageInfo imageInfo = identifiedImage;
            ExifProfile? exifProfile = imageInfo.Metadata.ExifProfile;
            GeoTiffTagSnapshot? tiffTags = await GeoTiffTagReader.TryReadAsync(sourcePath, cancellationToken);

            double[]? tiePoints = TryGetDoubleArray(exifProfile, ExifTag.ModelTiePoint)
                ?? tiffTags?.ModelTiePoint;
            double[]? pixelScale = TryGetDoubleArray(exifProfile, ExifTag.PixelScale)
                ?? tiffTags?.PixelScale;
            double[]? modelTransform = TryGetDoubleArray(exifProfile, ExifTag.ModelTransform)
                ?? tiffTags?.ModelTransform;
            ushort[]? geoKeyDirectory = TryGetUnsignedShortArray(exifProfile, "GeoKeyDirectoryTag")
                ?? tiffTags?.GeoKeyDirectory;
            double[]? geoDoubleParams = TryGetNamedDoubleArray(exifProfile, "GeoDoubleParamsTag")
                ?? tiffTags?.GeoDoubleParams;
            string? geoAsciiParams = TryGetNamedString(exifProfile, "GeoAsciiParamsTag")
                ?? tiffTags?.GeoAsciiParams;

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
        catch (Exception exception) when (IsCandidateRasterReadFailure(exception))
        {
            return null;
        }
    }

    public static async Task<GeoReferencedRasterMetadata?> TryReadMetadataAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek)
        {
            return null;
        }

        ImageInfo imageInfo;
        try
        {
            stream.Seek(0, SeekOrigin.Begin);
            ImageInfo? identifiedImage = await Image.IdentifyAsync(stream, cancellationToken);
            if (identifiedImage is null)
            {
                return null;
            }

            imageInfo = identifiedImage;
            ExifProfile? exifProfile = imageInfo.Metadata.ExifProfile;
            stream.Seek(0, SeekOrigin.Begin);
            GeoTiffTagSnapshot? tiffTags = await GeoTiffTagReader.TryReadAsync(stream, cancellationToken);

            double[]? tiePoints = TryGetDoubleArray(exifProfile, ExifTag.ModelTiePoint)
                ?? tiffTags?.ModelTiePoint;
            double[]? pixelScale = TryGetDoubleArray(exifProfile, ExifTag.PixelScale)
                ?? tiffTags?.PixelScale;
            double[]? modelTransform = TryGetDoubleArray(exifProfile, ExifTag.ModelTransform)
                ?? tiffTags?.ModelTransform;
            ushort[]? geoKeyDirectory = TryGetUnsignedShortArray(exifProfile, "GeoKeyDirectoryTag")
                ?? tiffTags?.GeoKeyDirectory;
            double[]? geoDoubleParams = TryGetNamedDoubleArray(exifProfile, "GeoDoubleParamsTag")
                ?? tiffTags?.GeoDoubleParams;
            string? geoAsciiParams = TryGetNamedString(exifProfile, "GeoAsciiParamsTag")
                ?? tiffTags?.GeoAsciiParams;

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
        catch (Exception exception) when (IsCandidateRasterReadFailure(exception))
        {
            return null;
        }
    }

    private static bool IsCandidateRasterReadFailure(Exception exception) =>
        exception is UnknownImageFormatException
            or IOException
            or UnauthorizedAccessException
            or SecurityException
            or InvalidDataException
            or OverflowException
            or ArgumentOutOfRangeException;

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
        if (geographicBounds is null || string.IsNullOrWhiteSpace(coordinateSystemIdentifier))
        {
            return null;
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
            return TryResolveUserDefinedCoordinateSystemIdentifier(geoAsciiParams);
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
                if (valueOffset == 32767)
                {
                    return TryResolveUserDefinedCoordinateSystemIdentifier(geoAsciiParams);
                }

                return $"EPSG:{valueOffset}";
            }
        }

        return null;
    }

    private static string? TryResolveUserDefinedCoordinateSystemIdentifier(string? geoAsciiParams)
    {
        if (string.IsNullOrWhiteSpace(geoAsciiParams))
        {
            return null;
        }

        foreach (string rawToken in geoAsciiParams.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string token = rawToken.Trim();
            if (token.Length == 0)
            {
                continue;
            }

            Match epsgMatch = GeoAsciiEpsgRegex.Match(token);
            if (epsgMatch.Success && int.TryParse(epsgMatch.Groups["code"].Value, out int epsgCode))
            {
                return $"EPSG:{epsgCode}";
            }

            if (token.Equals("WGS 84 / Pseudo-Mercator", StringComparison.OrdinalIgnoreCase)
                || token.Equals("WGS 84 / Web Mercator", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:3857";
            }

            if (token.Equals("WGS 84", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:4326";
            }

            Match japanPlaneMatch = JapanPlaneRectangularRegex.Match(token);
            if (japanPlaneMatch.Success
                && TryParseJapanPlaneRectangularZoneOrdinal(japanPlaneMatch.Groups["zone"].Value, out int zone))
            {
                return $"EPSG:{6668 + zone}";
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

        if (string.Equals(coordinateSystemIdentifier, "EPSG:3857", StringComparison.OrdinalIgnoreCase))
        {
            (double webMercatorMinLatitude, double webMercatorMinLongitude) = ReverseWebMercator(modelBounds.MinX, modelBounds.MinY);
            (double webMercatorMaxLatitude, double webMercatorMaxLongitude) = ReverseWebMercator(modelBounds.MaxX, modelBounds.MaxY);
            return new GeographicRectangle(
                MinLatitude: Math.Min(webMercatorMinLatitude, webMercatorMaxLatitude),
                MaxLatitude: Math.Max(webMercatorMinLatitude, webMercatorMaxLatitude),
                MinLongitude: Math.Min(webMercatorMinLongitude, webMercatorMaxLongitude),
                MaxLongitude: Math.Max(webMercatorMinLongitude, webMercatorMaxLongitude));
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

    private static (double Latitude, double Longitude) ReverseWebMercator(double x, double y)
    {
        double longitude = (x / WebMercatorEarthRadiusMeters) * (180.0 / Math.PI);
        double latitude = Math.Atan(Math.Sinh(y / WebMercatorEarthRadiusMeters)) * (180.0 / Math.PI);
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

    private static bool TryParseJapanPlaneRectangularZoneOrdinal(
        string token,
        out int zone)
    {
        if (int.TryParse(token, out zone))
        {
            return zone is >= 1 and <= 19;
        }

        zone = token.ToUpperInvariant() switch
        {
            "I" => 1,
            "II" => 2,
            "III" => 3,
            "IV" => 4,
            "V" => 5,
            "VI" => 6,
            "VII" => 7,
            "VIII" => 8,
            "IX" => 9,
            "X" => 10,
            "XI" => 11,
            "XII" => 12,
            "XIII" => 13,
            "XIV" => 14,
            "XV" => 15,
            "XVI" => 16,
            "XVII" => 17,
            "XVIII" => 18,
            "XIX" => 19,
            _ => 0,
        };

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

    private static double[]? TryGetDoubleArray(ExifProfile? exifProfile, ExifTag<double[]> tag)
    {
        if (exifProfile is null)
        {
            return null;
        }

        return exifProfile.TryGetValue(tag, out IExifValue<double[]>? exifValue)
            ? exifValue.Value
            : null;
    }

    private static ushort[]? TryGetUnsignedShortArray(ExifProfile? exifProfile, string tagName)
    {
        if (exifProfile is null)
        {
            return null;
        }

        object? value = TryGetNamedValue(exifProfile, tagName);
        return value switch
        {
            ushort[] ushortArray => ushortArray,
            short[] shortArray => shortArray.Select(static item => unchecked((ushort)item)).ToArray(),
            _ => null,
        };
    }

    private static double[]? TryGetNamedDoubleArray(ExifProfile? exifProfile, string tagName)
    {
        if (exifProfile is null)
        {
            return null;
        }

        return TryGetNamedValue(exifProfile, tagName) as double[];
    }

    private static string? TryGetNamedString(ExifProfile? exifProfile, string tagName)
    {
        if (exifProfile is null)
        {
            return null;
        }

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
    private const double WebMercatorEarthRadiusMeters = 6_378_137.0;

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

    private static readonly Regex GeoAsciiEpsgRegex = new(
        @"EPSG[:\s]+(?<code>\d{4,5})",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex JapanPlaneRectangularRegex = new(
        @"Japan Plane Rectangular CS\s+(?<zone>[IVX]+|\d+)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly record struct ModelSpaceRectangle(
        double MinX,
        double MinY,
        double MaxX,
        double MaxY);
}
