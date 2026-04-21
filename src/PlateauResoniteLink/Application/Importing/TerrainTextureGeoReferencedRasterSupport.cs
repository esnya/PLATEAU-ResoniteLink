using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using GeographicLib;
using GeographicLib.Projections;

using PlateauResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PlateauResoniteLink.Application.Importing;

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

internal sealed record GeoTiffTagSnapshot(
    double[]? ModelTiePoint,
    double[]? PixelScale,
    double[]? ModelTransform,
    ushort[]? GeoKeyDirectory,
    double[]? GeoDoubleParams,
    string? GeoAsciiParams);

internal static class GeoTiffTagReader
{
    private const ushort ClassicTiffMagic = 42;
    private const ushort BigTiffMagic = 43;
    private const ushort TypeAscii = 2;
    private const ushort TypeShort = 3;
    private const ushort TypeLong = 4;
    private const ushort TypeDouble = 12;
    private const ushort PixelScaleTag = 33550;
    private const ushort ModelTiePointTag = 33922;
    private const ushort ModelTransformTag = 34264;
    private const ushort GeoKeyDirectoryTag = 34735;
    private const ushort GeoDoubleParamsTag = 34736;
    private const ushort GeoAsciiParamsTag = 34737;

    public static async Task<GeoTiffTagSnapshot?> TryReadAsync(string sourcePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return TryRead(bytes);
    }

    internal static GeoTiffTagSnapshot? TryRead(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 8)
        {
            return null;
        }

        bool littleEndian = bytes[0] == (byte)'I' && bytes[1] == (byte)'I';
        if (!littleEndian && !(bytes[0] == (byte)'M' && bytes[1] == (byte)'M'))
        {
            return null;
        }

        ushort magic = ReadUInt16(bytes, 2, littleEndian);
        return magic switch
        {
            ClassicTiffMagic => TryReadClassic(bytes, littleEndian),
            BigTiffMagic => TryReadBigTiff(bytes, littleEndian),
            _ => null,
        };
    }

    private static GeoTiffTagSnapshot? TryReadClassic(ReadOnlySpan<byte> bytes, bool littleEndian)
    {
        uint ifdOffset = ReadUInt32(bytes, 4, littleEndian);
        if (ifdOffset >= bytes.Length || ifdOffset + 2 > bytes.Length)
        {
            return null;
        }

        ushort entryCount = ReadUInt16(bytes, checked((int)ifdOffset), littleEndian);
        int entriesOffset = checked((int)ifdOffset) + 2;
        Dictionary<ushort, object> values = [];
        for (int index = 0; index < entryCount; index++)
        {
            int entryOffset = entriesOffset + (index * 12);
            if (entryOffset + 12 > bytes.Length)
            {
                break;
            }

            TryReadClassicEntry(bytes, entryOffset, littleEndian, values);
        }

        return ToSnapshot(values);
    }

    private static GeoTiffTagSnapshot? TryReadBigTiff(ReadOnlySpan<byte> bytes, bool littleEndian)
    {
        if (bytes.Length < 16)
        {
            return null;
        }

        ushort offsetSize = ReadUInt16(bytes, 4, littleEndian);
        if (offsetSize != 8)
        {
            return null;
        }

        ulong ifdOffset = ReadUInt64(bytes, 8, littleEndian);
        if (ifdOffset >= (ulong)bytes.Length || ifdOffset + 8 > (ulong)bytes.Length)
        {
            return null;
        }

        ulong entryCount = ReadUInt64(bytes, checked((int)ifdOffset), littleEndian);
        int entriesOffset = checked((int)ifdOffset) + 8;
        Dictionary<ushort, object> values = [];
        for (ulong index = 0; index < entryCount; index++)
        {
            int entryOffset = checked(entriesOffset + ((int)index * 20));
            if (entryOffset + 20 > bytes.Length)
            {
                break;
            }

            TryReadBigTiffEntry(bytes, entryOffset, littleEndian, values);
        }

        return ToSnapshot(values);
    }

    private static void TryReadClassicEntry(
        ReadOnlySpan<byte> bytes,
        int entryOffset,
        bool littleEndian,
        Dictionary<ushort, object> values)
    {
        ushort tag = ReadUInt16(bytes, entryOffset, littleEndian);
        ushort type = ReadUInt16(bytes, entryOffset + 2, littleEndian);
        uint count = ReadUInt32(bytes, entryOffset + 4, littleEndian);
        uint valueOrOffset = ReadUInt32(bytes, entryOffset + 8, littleEndian);
        if (!TryReadEntryValue(bytes, type, count, valueOrOffset, bytes.Slice(entryOffset + 8, 4), littleEndian, out object? value))
        {
            return;
        }

        values[tag] = value!;
    }

    private static void TryReadBigTiffEntry(
        ReadOnlySpan<byte> bytes,
        int entryOffset,
        bool littleEndian,
        Dictionary<ushort, object> values)
    {
        ushort tag = ReadUInt16(bytes, entryOffset, littleEndian);
        ushort type = ReadUInt16(bytes, entryOffset + 2, littleEndian);
        ulong count = ReadUInt64(bytes, entryOffset + 4, littleEndian);
        ulong valueOrOffset = ReadUInt64(bytes, entryOffset + 12, littleEndian);
        if (!TryReadEntryValue(bytes, type, count, valueOrOffset, bytes.Slice(entryOffset + 12, 8), littleEndian, out object? value))
        {
            return;
        }

        values[tag] = value!;
    }

    private static bool TryReadEntryValue(
        ReadOnlySpan<byte> bytes,
        ushort type,
        ulong count,
        ulong valueOrOffset,
        ReadOnlySpan<byte> inlineValueBytes,
        bool littleEndian,
        out object? value)
    {
        value = null;
        int typeSize = GetTypeSize(type);
        if (typeSize == 0 || count == 0 || count > int.MaxValue)
        {
            return false;
        }

        ulong byteLength = checked((ulong)typeSize * count);
        ReadOnlySpan<byte> rawValueBytes;
        if (byteLength <= (ulong)inlineValueBytes.Length)
        {
            rawValueBytes = inlineValueBytes[..(int)byteLength];
        }
        else
        {
            if (valueOrOffset > int.MaxValue || valueOrOffset + byteLength > (ulong)bytes.Length)
            {
                return false;
            }

            rawValueBytes = bytes.Slice((int)valueOrOffset, (int)byteLength);
        }

        value = type switch
        {
            TypeShort => ReadUInt16Array(rawValueBytes, (int)count, littleEndian),
            TypeLong => ReadUInt32Array(rawValueBytes, (int)count, littleEndian),
            TypeDouble => ReadDoubleArray(rawValueBytes, (int)count, littleEndian),
            TypeAscii => Encoding.ASCII.GetString(rawValueBytes).TrimEnd('\0'),
            _ => null,
        };
        return value is not null;
    }

    private static GeoTiffTagSnapshot? ToSnapshot(Dictionary<ushort, object> values)
    {
        values.TryGetValue(ModelTiePointTag, out object? modelTiePoint);
        values.TryGetValue(PixelScaleTag, out object? pixelScale);
        values.TryGetValue(ModelTransformTag, out object? modelTransform);
        values.TryGetValue(GeoKeyDirectoryTag, out object? geoKeyDirectory);
        values.TryGetValue(GeoDoubleParamsTag, out object? geoDoubleParams);
        values.TryGetValue(GeoAsciiParamsTag, out object? geoAsciiParams);

        if (modelTiePoint is null
            && pixelScale is null
            && modelTransform is null
            && geoKeyDirectory is null
            && geoDoubleParams is null
            && geoAsciiParams is null)
        {
            return null;
        }

        return new GeoTiffTagSnapshot(
            ConvertToDoubleArray(modelTiePoint),
            ConvertToDoubleArray(pixelScale),
            ConvertToDoubleArray(modelTransform),
            ConvertToUInt16Array(geoKeyDirectory),
            ConvertToDoubleArray(geoDoubleParams),
            geoAsciiParams as string);
    }

    private static double[]? ConvertToDoubleArray(object? value)
    {
        return value switch
        {
            double[] doubles => doubles,
            uint[] uints => uints.Select(static item => (double)item).ToArray(),
            ushort[] ushorts => ushorts.Select(static item => (double)item).ToArray(),
            _ => null,
        };
    }

    private static ushort[]? ConvertToUInt16Array(object? value)
    {
        return value switch
        {
            ushort[] ushorts => ushorts,
            uint[] uints => uints.Select(static item => checked((ushort)item)).ToArray(),
            _ => null,
        };
    }

    private static int GetTypeSize(ushort type)
    {
        return type switch
        {
            TypeAscii => 1,
            TypeShort => 2,
            TypeLong => 4,
            TypeDouble => 8,
            _ => 0,
        };
    }

    private static ushort[] ReadUInt16Array(ReadOnlySpan<byte> bytes, int count, bool littleEndian)
    {
        ushort[] values = new ushort[count];
        for (int index = 0; index < count; index++)
        {
            values[index] = ReadUInt16(bytes, index * 2, littleEndian);
        }

        return values;
    }

    private static uint[] ReadUInt32Array(ReadOnlySpan<byte> bytes, int count, bool littleEndian)
    {
        uint[] values = new uint[count];
        for (int index = 0; index < count; index++)
        {
            values[index] = ReadUInt32(bytes, index * 4, littleEndian);
        }

        return values;
    }

    private static double[] ReadDoubleArray(ReadOnlySpan<byte> bytes, int count, bool littleEndian)
    {
        double[] values = new double[count];
        for (int index = 0; index < count; index++)
        {
            ulong rawBits = ReadUInt64(bytes, index * 8, littleEndian);
            values[index] = BitConverter.Int64BitsToDouble(unchecked((long)rawBits));
        }

        return values;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset, bool littleEndian)
    {
        ReadOnlySpan<byte> slice = bytes.Slice(offset, 2);
        return littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(slice)
            : BinaryPrimitives.ReadUInt16BigEndian(slice);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset, bool littleEndian)
    {
        ReadOnlySpan<byte> slice = bytes.Slice(offset, 4);
        return littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(slice)
            : BinaryPrimitives.ReadUInt32BigEndian(slice);
    }

    private static ulong ReadUInt64(ReadOnlySpan<byte> bytes, int offset, bool littleEndian)
    {
        ReadOnlySpan<byte> slice = bytes.Slice(offset, 8);
        return littleEndian
            ? BinaryPrimitives.ReadUInt64LittleEndian(slice)
            : BinaryPrimitives.ReadUInt64BigEndian(slice);
    }
}
