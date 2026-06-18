using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace PlateauResoniteLink.Plateau.Application.Importing;

internal static class DatasetSourceFileStatsReader
{
    internal const int GeometryCoordinateDimension = 3;
    internal static async Task<DatasetSourceFileStats> ReadAsync(
        Stream stream,
        string relativePath,
        string packageName,
        IPlateauDatasetContentSource datasetSource,
        CancellationToken cancellationToken)
    {
        try
        {
            XmlReaderSettings settings = new()
            {
                Async = true,
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreComments = true,
                IgnoreWhitespace = true,
            };
            using XmlReader reader = XmlReader.Create(stream, settings);
            HashSet<int> lodLevels = [];
            HashSet<string> resolvedTexturePaths = new(StringComparer.OrdinalIgnoreCase);
            GeometryVramAccumulator geometry = new();
            int? parameterizedTextureDepth = null;
            LinearRingGeometryAccumulator? linearRingGeometry = null;

            while (await reader.ReadAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType == XmlNodeType.EndElement
                    && linearRingGeometry is not null
                    && linearRingGeometry.Depth == reader.Depth
                    && IsGmlElement(reader, "LinearRing"))
                {
                    linearRingGeometry.AddTo(geometry);
                    linearRingGeometry = null;
                    continue;
                }

                if (reader.NodeType == XmlNodeType.EndElement
                    && parameterizedTextureDepth == reader.Depth
                    && IsAppearanceElement(reader, "ParameterizedTexture"))
                {
                    parameterizedTextureDepth = null;
                    continue;
                }

                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (IsAppearanceElement(reader, "ParameterizedTexture"))
                {
                    parameterizedTextureDepth = reader.IsEmptyElement ? null : reader.Depth;
                    continue;
                }

                if (IsRecognizedCityGmlNamespace(reader.NamespaceURI)
                    && TryParseLodLevel(reader.LocalName, out int lodLevel))
                {
                    lodLevels.Add(lodLevel);
                }

                if (IsGmlElement(reader, "LinearRing"))
                {
                    linearRingGeometry = reader.IsEmptyElement
                        ? null
                        : new LinearRingGeometryAccumulator(reader.Depth);
                    continue;
                }

                if (IsGmlElement(reader, "posList"))
                {
                    string content = await ReadCurrentElementTextAsync(reader);
                    if (linearRingGeometry is not null)
                    {
                        linearRingGeometry.MarkPosListSeen();
                        AddPosListGeometry(content, geometry);
                    }

                    if (reader.NodeType == XmlNodeType.EndElement
                        && linearRingGeometry is not null
                        && linearRingGeometry.Depth == reader.Depth
                        && IsGmlElement(reader, "LinearRing"))
                    {
                        linearRingGeometry = null;
                    }

                    continue;
                }

                if (linearRingGeometry is not null
                    && !linearRingGeometry.HasPosList
                    && IsGmlElement(reader, "pos"))
                {
                    string content = await ReadCurrentElementTextAsync(reader);
                    linearRingGeometry.AddPosition(content);
                    if (reader.NodeType == XmlNodeType.EndElement
                        && linearRingGeometry.Depth == reader.Depth
                        && IsGmlElement(reader, "LinearRing"))
                    {
                        linearRingGeometry.AddTo(geometry);
                        linearRingGeometry = null;
                    }

                    continue;
                }

                if (parameterizedTextureDepth is not null
                    && IsAppearanceElement(reader, "imageURI"))
                {
                    string imageUri = (await reader.ReadElementContentAsStringAsync()).Trim();
                    string? resolvedPath = datasetSource.ResolveRelativePath(relativePath, imageUri);
                    if (resolvedPath is not null
                        && IsSupportedRasterImagePath(resolvedPath))
                    {
                        resolvedTexturePaths.Add(resolvedPath);
                    }
                }
            }

            return new DatasetSourceFileStats(
                lodLevels.OrderBy(static lod => lod).ToArray(),
                resolvedTexturePaths.OrderBy(static path => path, StringComparer.Ordinal).ToArray(),
                DatasetGeometryVramEstimator.CreatePackageEstimate(packageName, geometry));
        }
        catch (XmlException)
        {
            return new DatasetSourceFileStats([], [], DatasetGeometryVramEstimator.CreatePackageEstimate(packageName, new GeometryVramAccumulator()));
        }
    }

    private static async Task<string> ReadCurrentElementTextAsync(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        while (await reader.ReadAsync())
        {
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                break;
            }

            if (reader.NodeType is XmlNodeType.Text
                or XmlNodeType.CDATA
                or XmlNodeType.Whitespace
                or XmlNodeType.SignificantWhitespace)
            {
                builder.Append(reader.Value);
            }
        }

        return builder.ToString();
    }

    private static bool IsRecognizedCityGmlNamespace(string namespaceUri)
    {
        return !string.IsNullOrWhiteSpace(namespaceUri)
            && namespaceUri.Contains("citygml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseLodLevel(string localName, out int lodLevel)
    {
        lodLevel = 0;
        if (!localName.StartsWith("lod", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int digitStart = 3;
        int digitLength = 0;
        while (digitStart + digitLength < localName.Length
            && char.IsDigit(localName[digitStart + digitLength]))
        {
            digitLength++;
        }

        return digitLength > 0
            && int.TryParse(
                localName.AsSpan(digitStart, digitLength),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out lodLevel);
    }

    private static bool IsGmlElement(XmlReader reader, string localName)
    {
        return string.Equals(reader.LocalName, localName, StringComparison.Ordinal)
            && reader.NamespaceURI.Contains("opengis.net/gml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAppearanceElement(XmlReader reader, string localName)
    {
        return string.Equals(reader.LocalName, localName, StringComparison.Ordinal)
            && reader.NamespaceURI.Contains("citygml/appearance", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedRasterImagePath(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".png", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddPosListGeometry(string coordinateText, GeometryVramAccumulator geometry)
    {
        if (!TryReadParserEquivalentLinearRingTokens(coordinateText, out GmlLinearRingTokenSequence? ringTokens))
        {
            return;
        }

        PosListGeometry posListGeometry = InspectLinearRingTokens(ringTokens);
        long positionCount = posListGeometry.PositionCount;
        if (positionCount <= 0)
        {
            return;
        }

        long effectiveVertexCount = posListGeometry.IsClosedRing ? positionCount - 1 : positionCount;
        AddPolygonFanGeometry(effectiveVertexCount, geometry);
    }

    internal static void AddPolygonFanGeometry(long effectiveVertexCount, GeometryVramAccumulator geometry)
    {
        geometry.PositionCount += effectiveVertexCount;
        if (effectiveVertexCount >= 3)
        {
            geometry.TriangleCount += effectiveVertexCount - 2;
        }
    }

    private static bool TryReadParserEquivalentLinearRingTokens(
        string coordinateText,
        [NotNullWhen(true)] out GmlLinearRingTokenSequence? ringTokens)
    {
        GmlPositionToken? firstPosition = null;
        GmlPositionToken? lastPosition = null;
        long positionCount = 0;
        double x = 0.0;
        double y = 0.0;
        double z = 0.0;
        int coordinateValueCount = 0;
        int tokenStart = -1;

        for (int index = 0; index <= coordinateText.Length; index++)
        {
            bool isTokenEnd = index == coordinateText.Length || char.IsWhiteSpace(coordinateText[index]);
            if (!isTokenEnd)
            {
                if (tokenStart < 0)
                {
                    tokenStart = index;
                }

                continue;
            }

            if (tokenStart < 0)
            {
                continue;
            }

            ReadOnlySpan<char> token = coordinateText.AsSpan(tokenStart, index - tokenStart);
            int ordinateIndex = coordinateValueCount % GeometryCoordinateDimension;
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double coordinate))
            {
                ringTokens = null;
                return false;
            }

            switch (ordinateIndex)
            {
                case 0:
                    x = coordinate;
                    break;
                case 1:
                    y = coordinate;
                    break;
                case 2:
                    z = coordinate;
                    break;
            }

            if (ordinateIndex == GeometryCoordinateDimension - 1)
            {
                GmlPositionToken position = new(x, y, z);
                firstPosition ??= position;
                lastPosition = position;
                positionCount++;
            }

            coordinateValueCount++;
            tokenStart = -1;
        }

        if (positionCount <= 0)
        {
            ringTokens = null;
            return false;
        }

        ringTokens = new GmlLinearRingTokenSequence(
            positionCount,
            firstPosition,
            lastPosition);
        return true;
    }

    private static PosListGeometry InspectLinearRingTokens(GmlLinearRingTokenSequence ringTokens)
    {
        bool isClosedRing = ringTokens.PositionCount > 1
            && ringTokens.FirstPosition is { } firstPosition
            && ringTokens.LastPosition is { } lastPosition
            && AreSamePosition(firstPosition, lastPosition);
        return new PosListGeometry(ringTokens.PositionCount, isClosedRing);
    }

    internal static bool AreSamePosition(GmlPositionToken left, GmlPositionToken right)
    {
        return Math.Abs(left.X - right.X) < 1e-8
            && Math.Abs(left.Y - right.Y) < 1e-8
            && Math.Abs(left.Z - right.Z) < 1e-8;
    }

    internal static bool AreSamePosition(double[] left, double[] right)
    {
        return left.Length == GeometryCoordinateDimension
            && right.Length == GeometryCoordinateDimension
            && AreSamePosition(
                new GmlPositionToken(left[0], left[1], left[2]),
                new GmlPositionToken(right[0], right[1], right[2]));
    }
}

internal sealed record DatasetSourceFileStats(
    IReadOnlyList<int> LodLevels,
    IReadOnlyList<string> ResolvedTexturePaths,
    DatasetPackageGeometryVramEstimate Geometry);

internal readonly record struct PosListGeometry(
    long PositionCount,
    bool IsClosedRing);

internal readonly record struct GmlPositionToken(
    double X,
    double Y,
    double Z);

internal sealed record GmlLinearRingTokenSequence(
    long PositionCount,
    GmlPositionToken? FirstPosition,
    GmlPositionToken? LastPosition);

internal sealed class LinearRingGeometryAccumulator(int depth)
{
    private double[]? firstPosition;
    private double[]? lastPosition;
    private long positionCount;

    public int Depth { get; } = depth;

    public bool HasPosList { get; private set; }

    public void MarkPosListSeen()
    {
        HasPosList = true;
    }

    public void AddPosition(string coordinateText)
    {
        double[] position = new double[DatasetSourceFileStatsReader.GeometryCoordinateDimension];
        int parsedOrdinateCount = 0;
        int coordinateValueCount = 0;
        int tokenStart = -1;

        for (int index = 0; index <= coordinateText.Length; index++)
        {
            bool isTokenEnd = index == coordinateText.Length || char.IsWhiteSpace(coordinateText[index]);
            if (!isTokenEnd)
            {
                if (tokenStart < 0)
                {
                    tokenStart = index;
                }

                continue;
            }

            if (tokenStart < 0)
            {
                continue;
            }

            if (coordinateValueCount < DatasetSourceFileStatsReader.GeometryCoordinateDimension)
            {
                if (!double.TryParse(
                    coordinateText.AsSpan(tokenStart, index - tokenStart),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double coordinate))
                {
                    return;
                }

                position[coordinateValueCount] = coordinate;
                parsedOrdinateCount++;
            }

            coordinateValueCount++;
            tokenStart = -1;
        }

        if (coordinateValueCount != DatasetSourceFileStatsReader.GeometryCoordinateDimension
            || parsedOrdinateCount != DatasetSourceFileStatsReader.GeometryCoordinateDimension)
        {
            return;
        }

        firstPosition ??= position;
        lastPosition = position;
        positionCount++;
    }

    public void AddTo(GeometryVramAccumulator geometry)
    {
        if (HasPosList || positionCount <= 0)
        {
            return;
        }

        bool isClosedRing = firstPosition is not null
            && lastPosition is not null
            && firstPosition.Length == lastPosition.Length
            && positionCount > 1
            && DatasetSourceFileStatsReader.AreSamePosition(firstPosition, lastPosition);
        long effectiveVertexCount = isClosedRing ? positionCount - 1 : positionCount;
        DatasetSourceFileStatsReader.AddPolygonFanGeometry(effectiveVertexCount, geometry);
    }
}
