using System.Buffers.Binary;

using GeographicLib;
using GeographicLib.Projections;

using Plateau.ResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Plateau.ResoniteLink.Application.Importing;

internal static class TerrainTextureGeoReferencedRasterMetadataReader
{
    private const int ModelPixelScaleTagId = 33550;
    private const int ModelTiePointTagId = 33922;
    private const int ModelTransformTagId = 34264;
    private const int GeoKeyDirectoryTagId = 34735;
    private const int GeoDoubleParamsTagId = 34736;
    private const int GeoAsciiParamsTagId = 34737;
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
        GeoTiffTagBundle? geoTiffTags = GeoTiffTagReader.TryRead(sourcePath);

        double[]? tiePoints = TryGetDoubleArray(exifProfile, ExifTag.ModelTiePoint, ModelTiePointTagId)
            ?? geoTiffTags?.ModelTiePoint;
        double[]? pixelScale = TryGetDoubleArray(exifProfile, ExifTag.PixelScale, ModelPixelScaleTagId)
            ?? geoTiffTags?.ModelPixelScale;
        double[]? modelTransform = TryGetDoubleArray(exifProfile, ExifTag.ModelTransform, ModelTransformTagId)
            ?? geoTiffTags?.ModelTransform;
        ushort[]? geoKeyDirectory = TryGetUnsignedShortArray(exifProfile, "GeoKeyDirectoryTag", GeoKeyDirectoryTagId)
            ?? geoTiffTags?.GeoKeyDirectory;
        double[]? geoDoubleParams = TryGetNamedDoubleArray(exifProfile, "GeoDoubleParamsTag", GeoDoubleParamsTagId)
            ?? geoTiffTags?.GeoDoubleParams;
        string? geoAsciiParams = TryGetNamedString(exifProfile, "GeoAsciiParamsTag", GeoAsciiParamsTagId)
            ?? geoTiffTags?.GeoAsciiParams;

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
            return TryResolveCoordinateSystemIdentifierFromAuxiliaryMetadata(geoAsciiParams);
        }

        _ = geoDoubleParams;

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
                    return TryResolveCoordinateSystemIdentifierFromAuxiliaryMetadata(geoAsciiParams);
                }

                return $"EPSG:{valueOffset}";
            }
        }

        return TryResolveCoordinateSystemIdentifierFromAuxiliaryMetadata(geoAsciiParams);
    }

    private static string? TryResolveCoordinateSystemIdentifierFromAuxiliaryMetadata(string? geoAsciiParams)
    {
        if (string.IsNullOrWhiteSpace(geoAsciiParams))
        {
            return null;
        }

        if (geoAsciiParams.Contains("Pseudo-Mercator", StringComparison.OrdinalIgnoreCase))
        {
            return "EPSG:3857";
        }

        if (TryResolveJapanPlaneRectangularCoordinateSystemIdentifier(geoAsciiParams, out string? japanPlaneCoordinateSystemIdentifier))
        {
            return japanPlaneCoordinateSystemIdentifier;
        }

        if (geoAsciiParams.Contains("WGS 84", StringComparison.OrdinalIgnoreCase))
        {
            return "EPSG:4326";
        }

        return null;
    }

    private static bool TryResolveJapanPlaneRectangularCoordinateSystemIdentifier(
        string geoAsciiParams,
        out string? coordinateSystemIdentifier)
    {
        const string marker = "Japan Plane Rectangular CS ";
        int markerIndex = geoAsciiParams.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            coordinateSystemIdentifier = null;
            return false;
        }

        int zoneStartIndex = markerIndex + marker.Length;
        int zoneEndIndex = geoAsciiParams.IndexOf('|', zoneStartIndex);
        if (zoneEndIndex < 0)
        {
            zoneEndIndex = geoAsciiParams.Length;
        }

        string zoneToken = geoAsciiParams[zoneStartIndex..zoneEndIndex].Trim();
        if (!TryParseJapanPlaneRectangularZoneToken(zoneToken, out int zone))
        {
            coordinateSystemIdentifier = null;
            return false;
        }

        coordinateSystemIdentifier = $"EPSG:{6668 + zone}";
        return true;
    }

    private static bool TryParseJapanPlaneRectangularZoneToken(
        string zoneToken,
        out int zone)
    {
        if (int.TryParse(zoneToken, out zone))
        {
            return zone is >= 1 and <= 19;
        }

        zone = zoneToken.ToUpperInvariant() switch
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
            (double mercatorMinLatitude, double mercatorMinLongitude) = ReverseWebMercator(modelBounds.MinX, modelBounds.MinY);
            (double mercatorMaxLatitude, double mercatorMaxLongitude) = ReverseWebMercator(modelBounds.MaxX, modelBounds.MaxY);
            return new GeographicRectangle(
                MinLatitude: Math.Min(mercatorMinLatitude, mercatorMaxLatitude),
                MaxLatitude: Math.Max(mercatorMinLatitude, mercatorMaxLatitude),
                MinLongitude: Math.Min(mercatorMinLongitude, mercatorMaxLongitude),
                MaxLongitude: Math.Max(mercatorMinLongitude, mercatorMaxLongitude));
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
        double normalizedX = 0.5 + (x / WebMercatorEarthCircumferenceMeters);
        double normalizedY = 0.5 - (y / WebMercatorEarthCircumferenceMeters);
        return (
            WebMercatorTileMath.NormalizedYToLatitude(normalizedY),
            WebMercatorTileMath.NormalizedXToLongitude(normalizedX));
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

    private static double[]? TryGetDoubleArray(ExifProfile? exifProfile, ExifTag<double[]> tag, int fallbackTagId)
    {
        if (exifProfile is null)
        {
            return null;
        }

        if (exifProfile.TryGetValue(tag, out IExifValue<double[]>? exifValue))
        {
            return exifValue.Value;
        }

        object? fallbackValue = TryGetNamedValue(exifProfile, tag.ToString(), fallbackTagId);
        return fallbackValue switch
        {
            double[] doubleArray => doubleArray,
            float[] floatArray => floatArray.Select(static value => (double)value).ToArray(),
            decimal[] decimalArray => decimalArray.Select(static value => (double)value).ToArray(),
            _ => null,
        };
    }

    private static ushort[]? TryGetUnsignedShortArray(ExifProfile? exifProfile, string tagName, int tagId)
    {
        if (exifProfile is null)
        {
            return null;
        }

        object? value = TryGetNamedValue(exifProfile, tagName, tagId);
        return value switch
        {
            ushort[] ushortArray => ushortArray,
            short[] shortArray => shortArray.Select(static item => unchecked((ushort)item)).ToArray(),
            byte[] byteArray => byteArray.Select(static item => (ushort)item).ToArray(),
            _ => null,
        };
    }

    private static double[]? TryGetNamedDoubleArray(ExifProfile? exifProfile, string tagName, int tagId)
    {
        if (exifProfile is null)
        {
            return null;
        }

        object? value = TryGetNamedValue(exifProfile, tagName, tagId);
        return value switch
        {
            double[] doubleArray => doubleArray,
            float[] floatArray => floatArray.Select(static item => (double)item).ToArray(),
            decimal[] decimalArray => decimalArray.Select(static item => (double)item).ToArray(),
            _ => null,
        };
    }

    private static string? TryGetNamedString(ExifProfile? exifProfile, string tagName, int tagId)
    {
        if (exifProfile is null)
        {
            return null;
        }

        return TryGetNamedValue(exifProfile, tagName, tagId) as string;
    }

    private static object? TryGetNamedValue(ExifProfile exifProfile, string tagName, int tagId)
    {
        foreach (IExifValue value in exifProfile.Values)
        {
            if (!string.Equals(value.Tag.ToString(), tagName, StringComparison.Ordinal)
                && !HasTagIdentifier(value.Tag, tagId))
            {
                continue;
            }

            return value.GetType().GetProperty("Value")?.GetValue(value);
        }

        return null;
    }

    private static bool HasTagIdentifier(object tag, int tagId)
    {
        return tag switch
        {
            ushort ushortTag => ushortTag == tagId,
            short shortTag => shortTag == tagId,
            int intTag => intTag == tagId,
            long longTag => longTag == tagId,
            _ => TryConvertTagIdentifier(tag, out int convertedTagId) && convertedTagId == tagId,
        };
    }

    private static bool TryConvertTagIdentifier(object tag, out int tagId)
    {
        try
        {
            tagId = Convert.ToInt32(tag, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch (InvalidCastException) when (tag is not null)
        {
            tagId = 0;
            return false;
        }
        catch (FormatException) when (tag is not null)
        {
            tagId = 0;
            return false;
        }
        catch (OverflowException) when (tag is not null)
        {
            tagId = 0;
            return false;
        }
        catch (NotSupportedException) when (tag is not null)
        {
            tagId = 0;
            return false;
        }
    }

    private const double JapanPlaneRectangularCentralScale = 0.9999;
    private const double WebMercatorEarthCircumferenceMeters = 2.0 * Math.PI * 6_378_137.0;

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

internal sealed record GeoTiffTagBundle(
    double[]? ModelPixelScale,
    double[]? ModelTiePoint,
    double[]? ModelTransform,
    ushort[]? GeoKeyDirectory,
    double[]? GeoDoubleParams,
    string? GeoAsciiParams);

internal static class GeoTiffTagReader
{
    private const ushort ClassicTiffMagic = 42;
    private const ushort ModelPixelScaleTagId = 33550;
    private const ushort ModelTiePointTagId = 33922;
    private const ushort ModelTransformTagId = 34264;
    private const ushort GeoKeyDirectoryTagId = 34735;
    private const ushort GeoDoubleParamsTagId = 34736;
    private const ushort GeoAsciiParamsTagId = 34737;

    public static GeoTiffTagBundle? TryRead(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return null;
        }

        using FileStream stream = File.OpenRead(sourcePath);
        return TryRead(stream);
    }

    internal static GeoTiffTagBundle? TryRead(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek || stream.Length < 8)
        {
            return null;
        }

        Span<byte> header = stackalloc byte[8];
        stream.Position = 0;
        if (stream.Read(header) != header.Length)
        {
            return null;
        }

        bool isLittleEndian = header[0] == (byte)'I' && header[1] == (byte)'I';
        if (!isLittleEndian && !(header[0] == (byte)'M' && header[1] == (byte)'M'))
        {
            return null;
        }

        ushort magic = ReadUInt16(header[2..4], isLittleEndian);
        if (magic != ClassicTiffMagic)
        {
            return null;
        }

        uint ifdOffset = ReadUInt32(header[4..8], isLittleEndian);
        if (ifdOffset >= stream.Length)
        {
            return null;
        }

        stream.Position = ifdOffset;
        Span<byte> entryCountBytes = stackalloc byte[2];
        if (stream.Read(entryCountBytes) != entryCountBytes.Length)
        {
            return null;
        }

        ushort entryCount = ReadUInt16(entryCountBytes, isLittleEndian);
        double[]? modelPixelScale = null;
        double[]? modelTiePoint = null;
        double[]? modelTransform = null;
        ushort[]? geoKeyDirectory = null;
        double[]? geoDoubleParams = null;
        string? geoAsciiParams = null;
        byte[] entryBytes = new byte[12];

        for (int index = 0; index < entryCount; index++)
        {
            if (stream.Read(entryBytes) != entryBytes.Length)
            {
                return null;
            }

            ushort tagId = ReadUInt16(entryBytes.AsSpan(0, 2), isLittleEndian);
            ushort fieldType = ReadUInt16(entryBytes.AsSpan(2, 2), isLittleEndian);
            uint valueCount = ReadUInt32(entryBytes.AsSpan(4, 4), isLittleEndian);
            int elementSize = GetElementSize(fieldType);
            if (elementSize == 0)
            {
                continue;
            }

            long byteCount = (long)elementSize * valueCount;
            if (byteCount <= 0)
            {
                continue;
            }

            byte[] valueBytes = new byte[byteCount];
            if (byteCount <= 4)
            {
                entryBytes[8..(8 + (int)byteCount)].CopyTo(valueBytes);
            }
            else
            {
                uint valueOffset = ReadUInt32(entryBytes.AsSpan(8, 4), isLittleEndian);
                if (valueOffset + byteCount > stream.Length)
                {
                    continue;
                }

                long resumePosition = stream.Position;
                stream.Position = valueOffset;
                if (stream.Read(valueBytes, 0, valueBytes.Length) != valueBytes.Length)
                {
                    return null;
                }

                stream.Position = resumePosition;
            }

            switch (tagId)
            {
                case ModelPixelScaleTagId:
                    modelPixelScale = fieldType == 12 ? ReadDoubleArray(valueBytes, valueCount, isLittleEndian) : null;
                    break;
                case ModelTiePointTagId:
                    modelTiePoint = fieldType == 12 ? ReadDoubleArray(valueBytes, valueCount, isLittleEndian) : null;
                    break;
                case ModelTransformTagId:
                    modelTransform = fieldType == 12 ? ReadDoubleArray(valueBytes, valueCount, isLittleEndian) : null;
                    break;
                case GeoKeyDirectoryTagId:
                    geoKeyDirectory = fieldType == 3 ? ReadUInt16Array(valueBytes, valueCount, isLittleEndian) : null;
                    break;
                case GeoDoubleParamsTagId:
                    geoDoubleParams = fieldType == 12 ? ReadDoubleArray(valueBytes, valueCount, isLittleEndian) : null;
                    break;
                case GeoAsciiParamsTagId:
                    geoAsciiParams = fieldType == 2 ? ReadAsciiString(valueBytes) : null;
                    break;
            }
        }

        return new GeoTiffTagBundle(
            modelPixelScale,
            modelTiePoint,
            modelTransform,
            geoKeyDirectory,
            geoDoubleParams,
            geoAsciiParams);
    }

    private static int GetElementSize(ushort fieldType)
    {
        return fieldType switch
        {
            2 => 1,
            3 => 2,
            12 => 8,
            _ => 0,
        };
    }

    private static ushort[] ReadUInt16Array(byte[] bytes, uint count, bool isLittleEndian)
    {
        ushort[] values = new ushort[count];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = ReadUInt16(bytes.AsSpan(index * 2, 2), isLittleEndian);
        }

        return values;
    }

    private static double[] ReadDoubleArray(byte[] bytes, uint count, bool isLittleEndian)
    {
        double[] values = new double[count];
        byte[] buffer = new byte[8];
        for (int index = 0; index < values.Length; index++)
        {
            bytes.AsSpan(index * 8, 8).CopyTo(buffer);
            if (!isLittleEndian)
            {
                Array.Reverse(buffer);
            }

            values[index] = BitConverter.ToDouble(buffer, 0);
        }

        return values;
    }

    private static string ReadAsciiString(byte[] bytes)
    {
        int terminatorIndex = Array.IndexOf(bytes, (byte)0);
        int length = terminatorIndex >= 0 ? terminatorIndex : bytes.Length;
        return System.Text.Encoding.ASCII.GetString(bytes, 0, length);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, bool isLittleEndian)
    {
        return isLittleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(bytes)
            : BinaryPrimitives.ReadUInt16BigEndian(bytes);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, bool isLittleEndian)
    {
        return isLittleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(bytes)
            : BinaryPrimitives.ReadUInt32BigEndian(bytes);
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
        (double v0, double v1) = GetVerticalCropRange(metadata, rasterBounds, intersection);

        int left = Math.Clamp((int)Math.Floor(u0 * sourceImage.Width), 0, sourceImage.Width - 1);
        int top = Math.Clamp((int)Math.Floor(v0 * sourceImage.Height), 0, sourceImage.Height - 1);
        int right = Math.Clamp((int)Math.Ceiling(u1 * sourceImage.Width), left + 1, sourceImage.Width);
        int bottom = Math.Clamp((int)Math.Ceiling(v1 * sourceImage.Height), top + 1, sourceImage.Height);

        return sourceImage.Clone(context => context.Crop(new Rectangle(left, top, right - left, bottom - top)));
    }

    private static (double Top, double Bottom) GetVerticalCropRange(
        GeoReferencedRasterMetadata metadata,
        GeographicRectangle rasterBounds,
        GeographicRectangle intersection)
    {
        if (string.Equals(metadata.CoordinateSystemIdentifier, "EPSG:3857", StringComparison.OrdinalIgnoreCase))
        {
            double rasterTop = WebMercatorTileMath.LatitudeToNormalizedY(rasterBounds.MaxLatitude);
            double rasterBottom = WebMercatorTileMath.LatitudeToNormalizedY(rasterBounds.MinLatitude);
            double intersectionTop = WebMercatorTileMath.LatitudeToNormalizedY(intersection.MaxLatitude);
            double intersectionBottom = WebMercatorTileMath.LatitudeToNormalizedY(intersection.MinLatitude);
            double height = rasterBottom - rasterTop;
            return (
                (intersectionTop - rasterTop) / height,
                (intersectionBottom - rasterTop) / height);
        }

        return (
            (rasterBounds.MaxLatitude - intersection.MaxLatitude) / (rasterBounds.MaxLatitude - rasterBounds.MinLatitude),
            (rasterBounds.MaxLatitude - intersection.MinLatitude) / (rasterBounds.MaxLatitude - rasterBounds.MinLatitude));
    }
}
