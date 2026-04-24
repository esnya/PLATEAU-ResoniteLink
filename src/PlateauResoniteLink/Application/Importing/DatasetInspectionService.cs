using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class DatasetInspectionService(IPlateauDatasetContentSourceFactory datasetContentSourceFactory)
{
    private const int BcBlockPixelSize = 4;
    private const int Bc1BlockBytes = 8;
    private const int Bc3BlockBytes = 16;
    private const int GeometryVertexBytesMin = 32;
    private const int GeometryVertexBytesMax = 64;
    private const int GeometryIndexBytes = 4;

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The inspection API stays instance-based so the CLI can depend on a single extensible service contract.")]
    public async Task<DatasetSearchResult> SearchAsync(
        string sourcePath,
        string meshCode,
        IReadOnlyList<string>? packageNames,
        CancellationToken cancellationToken = default)
    {
        try
        {
            IPlateauDatasetContentSource datasetSource = await datasetContentSourceFactory.CreateAsync(sourcePath, cancellationToken);
            LocalCityGmlSourceFileDiscoveryResult discovery = LocalCityGmlSourceFileDiscovery.Discover(
                datasetSource.EnumerateFiles(),
                meshCode,
                packageNames);

            DatasetSearchEntry[] entries = discovery.SourceFiles
                .Select(static descriptor => new DatasetSearchEntry(
                    descriptor.RelativePath,
                    descriptor.PackageName,
                    descriptor.MatchedMeshCode,
                    descriptor.RequiresMeshAreaFilter))
                .ToArray();

            return new DatasetSearchResult(
                entries,
                discovery.SelectedMeshCodes.ToArray());
        }
        catch (ArgumentException exception)
        {
            throw new PlateauImportValidationException([exception.Message]);
        }
    }

    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The inspection API stays instance-based so the CLI can depend on a single extensible service contract.")]
    public async Task<DatasetStatsResult> GetStatsAsync(
        string sourcePath,
        IReadOnlyList<string>? packageNames,
        CancellationToken cancellationToken = default)
    {
        try
        {
            IPlateauDatasetContentSource datasetSource = await datasetContentSourceFactory.CreateAsync(sourcePath, cancellationToken);
            LocalCityGmlDatasetSourceFileCandidate[] candidates = LocalCityGmlSourceFileDiscovery
                .EnumerateCandidates(datasetSource.EnumerateFiles(), packageNames)
                .Where(static candidate => candidate.IsRequestedPackage)
                .ToArray();

            Dictionary<string, int> packageCounts = candidates
                .GroupBy(static candidate => candidate.PackageName, StringComparer.Ordinal)
                .OrderBy(static group => group.Key, StringComparer.Ordinal)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.Count(),
                    StringComparer.Ordinal);

            Dictionary<string, int> meshCodeCounts = candidates
                .Select(static candidate => ResolveRepresentativeMeshCode(candidate))
                .Where(static meshCode => meshCode is not null)
                .Select(static meshCode => meshCode!)
                .GroupBy(static meshCode => meshCode, StringComparer.Ordinal)
                .OrderBy(static group => group.Key, StringComparer.Ordinal)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.Count(),
                    StringComparer.Ordinal);

            Dictionary<int, int> lodCounts = [];
            int filesWithoutDetectedLod = 0;
            Dictionary<string, HashSet<string>> textureReferencesByPackage = new(StringComparer.Ordinal);
            Dictionary<string, GeometryVramAccumulator> geometryByPackage = new(StringComparer.Ordinal);

            foreach (LocalCityGmlDatasetSourceFileCandidate candidate in candidates)
            {
                await using Stream stream = await datasetSource.OpenReadAsync(candidate.RelativePath, cancellationToken);
                DatasetSourceFileStats sourceFileStats = await ReadSourceFileStatsAsync(
                    stream,
                    candidate.RelativePath,
                    candidate.PackageName,
                    datasetSource,
                    cancellationToken);

                if (sourceFileStats.LodLevels.Count == 0)
                {
                    filesWithoutDetectedLod++;
                }

                foreach (int lodLevel in sourceFileStats.LodLevels)
                {
                    lodCounts.TryGetValue(lodLevel, out int currentCount);
                    lodCounts[lodLevel] = currentCount + 1;
                }

                if (sourceFileStats.ResolvedTexturePaths.Count > 0)
                {
                    if (!textureReferencesByPackage.TryGetValue(candidate.PackageName, out HashSet<string>? textureReferences))
                    {
                        textureReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        textureReferencesByPackage[candidate.PackageName] = textureReferences;
                    }

                    foreach (string texturePath in sourceFileStats.ResolvedTexturePaths)
                    {
                        textureReferences.Add(texturePath);
                    }
                }

                GeometryVramAccumulator geometryAccumulator = GetOrCreateGeometryAccumulator(
                    geometryByPackage,
                    candidate.PackageName);
                geometryAccumulator.PositionCount += sourceFileStats.Geometry.PositionCount;
                geometryAccumulator.TriangleCount += sourceFileStats.Geometry.TriangleCount;
            }

            DatasetTextureVramEstimate textureVramEstimate = await EstimateTextureVramAsync(
                datasetSource,
                textureReferencesByPackage,
                cancellationToken);

            DatasetGeometryVramEstimate geometryVramEstimate = CreateGeometryVramEstimate(geometryByPackage);

            return new DatasetStatsResult(
                candidates.Length,
                packageCounts,
                meshCodeCounts,
                lodCounts.OrderBy(static pair => pair.Key).ToDictionary(static pair => pair.Key, static pair => pair.Value),
                filesWithoutDetectedLod,
                new DatasetArchiveVramEstimate(
                    textureVramEstimate,
                    geometryVramEstimate,
                    textureVramEstimate.RendererTotalBytes + geometryVramEstimate.RendererBytesMin,
                    textureVramEstimate.RendererTotalBytes + geometryVramEstimate.RendererBytesMax));
        }
        catch (ArgumentException exception)
        {
            throw new PlateauImportValidationException([exception.Message]);
        }
    }

    private static async Task<DatasetSourceFileStats> ReadSourceFileStatsAsync(
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

            while (await reader.ReadAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();
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

                if (IsGmlElement(reader, "posList"))
                {
                    int dimension = TryParsePositiveInt(reader.GetAttribute("srsDimension"), out int parsedDimension)
                        ? parsedDimension
                        : 3;
                    string content = await reader.ReadElementContentAsStringAsync();
                    AddPosListGeometry(content, dimension, geometry);
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
                CreatePackageGeometryVramEstimate(packageName, geometry));
        }
        catch (XmlException)
        {
            return new DatasetSourceFileStats([], [], CreatePackageGeometryVramEstimate(packageName, new GeometryVramAccumulator()));
        }
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

    private static bool TryParsePositiveInt(string? value, out int result)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
            && result > 0;
    }

    private static void AddPosListGeometry(string coordinateText, int dimension, GeometryVramAccumulator geometry)
    {
        int coordinateValueCount = CountCoordinateValues(coordinateText);
        long positionCount = coordinateValueCount / dimension;
        if (positionCount <= 0)
        {
            return;
        }

        long effectiveVertexCount = positionCount > 1 ? positionCount - 1 : positionCount;
        geometry.PositionCount += effectiveVertexCount;
        if (effectiveVertexCount >= 3)
        {
            geometry.TriangleCount += effectiveVertexCount - 2;
        }
    }

    private static int CountCoordinateValues(string coordinateText)
    {
        int count = 0;
        bool inToken = false;
        foreach (char character in coordinateText)
        {
            if (char.IsWhiteSpace(character))
            {
                if (inToken)
                {
                    count++;
                    inToken = false;
                }

                continue;
            }

            inToken = true;
        }

        return inToken ? count + 1 : count;
    }

    private static async Task<DatasetTextureVramEstimate> EstimateTextureVramAsync(
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyDictionary<string, HashSet<string>> textureReferencesByPackage,
        CancellationToken cancellationToken)
    {
        Dictionary<string, DatasetTextureVramEntry> textureEntries = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, TextureVramAccumulator> packageAccumulators = textureReferencesByPackage.Keys
            .OrderBy(static packageName => packageName, StringComparer.Ordinal)
            .ToDictionary(static packageName => packageName, static _ => new TextureVramAccumulator(), StringComparer.Ordinal);

        foreach (string texturePath in textureReferencesByPackage.Values
            .SelectMany(static paths => paths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            DatasetTextureVramEntry? entry = await TryReadTextureVramEntryAsync(datasetSource, texturePath, cancellationToken);
            if (entry is not null)
            {
                textureEntries[texturePath] = entry;
            }
        }

        foreach ((string packageName, HashSet<string> texturePaths) in textureReferencesByPackage)
        {
            TextureVramAccumulator accumulator = packageAccumulators[packageName];
            foreach (string texturePath in texturePaths)
            {
                if (textureEntries.TryGetValue(texturePath, out DatasetTextureVramEntry? entry))
                {
                    accumulator.Add(entry);
                }
                else
                {
                    accumulator.MissingTextureReferenceCount++;
                }
            }
        }

        int referencedTextureCount = textureReferencesByPackage.Values
            .SelectMany(static paths => paths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        TextureVramAccumulator total = new();
        foreach (DatasetTextureVramEntry entry in textureEntries.Values)
        {
            total.Add(entry);
        }

        total.ReferencedTextureCount = referencedTextureCount;
        total.MissingTextureReferenceCount = total.ReferencedTextureCount - total.ResolvedTextureFileCount;

        return new DatasetTextureVramEstimate(
            total.ReferencedTextureCount,
            total.ResolvedTextureFileCount,
            total.MissingTextureReferenceCount,
            total.Bc1TextureCount,
            total.Bc3TextureCount,
            total.Bc1Bytes,
            total.Bc3Bytes,
            total.RendererTotalBytes,
            total.Rgba32PayloadBytes,
            packageAccumulators
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value.ToEstimate(),
                    StringComparer.Ordinal));
    }

    private static async Task<DatasetTextureVramEntry?> TryReadTextureVramEntryAsync(
        IPlateauDatasetContentSource datasetSource,
        string texturePath,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!datasetSource.FileExists(texturePath))
            {
                return null;
            }

            await using Stream stream = await datasetSource.OpenReadAsync(texturePath, cancellationToken);
            using Image<Rgba32> image = await Image.LoadAsync<Rgba32>(stream, cancellationToken);
            bool hasEffectiveAlpha = HasNonOpaquePixels(image);
            long rendererBytes = EstimateBlockCompressedTextureBytes(
                image.Width,
                image.Height,
                hasEffectiveAlpha ? Bc3BlockBytes : Bc1BlockBytes);

            return new DatasetTextureVramEntry(
                texturePath,
                image.Width,
                image.Height,
                hasEffectiveAlpha,
                rendererBytes,
                (long)image.Width * image.Height * 4);
        }
        catch (UnknownImageFormatException)
        {
            return null;
        }
        catch (InvalidImageContentException)
        {
            return null;
        }
    }

    private static bool HasNonOpaquePixels(Image<Rgba32> image)
    {
        bool hasNonOpaquePixels = false;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                ReadOnlySpan<Rgba32> row = accessor.GetRowSpan(y);
                foreach (Rgba32 pixel in row)
                {
                    if (pixel.A != byte.MaxValue)
                    {
                        hasNonOpaquePixels = true;
                        return;
                    }
                }
            }
        });
        return hasNonOpaquePixels;
    }

    private static long EstimateBlockCompressedTextureBytes(int width, int height, int blockBytes)
    {
        long totalBytes = 0;
        int mipWidth = width;
        int mipHeight = height;
        while (true)
        {
            long blocksWide = Math.Max(1, (mipWidth + BcBlockPixelSize - 1) / BcBlockPixelSize);
            long blocksHigh = Math.Max(1, (mipHeight + BcBlockPixelSize - 1) / BcBlockPixelSize);
            totalBytes += blocksWide * blocksHigh * blockBytes;

            if (mipWidth == 1 && mipHeight == 1)
            {
                return totalBytes;
            }

            mipWidth = Math.Max(1, mipWidth / 2);
            mipHeight = Math.Max(1, mipHeight / 2);
        }
    }

    private static DatasetGeometryVramEstimate CreateGeometryVramEstimate(
        IReadOnlyDictionary<string, GeometryVramAccumulator> geometryByPackage)
    {
        Dictionary<string, DatasetPackageGeometryVramEstimate> packageEstimates = geometryByPackage
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                static pair => pair.Key,
                static pair => CreatePackageGeometryVramEstimate(pair.Key, pair.Value),
                StringComparer.Ordinal);

        long positionCount = packageEstimates.Values.Sum(static estimate => estimate.PositionCount);
        long triangleCount = packageEstimates.Values.Sum(static estimate => estimate.TriangleCount);
        long vertexBufferBytesMin = positionCount * GeometryVertexBytesMin;
        long vertexBufferBytesMax = positionCount * GeometryVertexBytesMax;
        long indexBufferBytes = triangleCount * 3 * GeometryIndexBytes;

        return new DatasetGeometryVramEstimate(
            positionCount,
            triangleCount,
            vertexBufferBytesMin,
            vertexBufferBytesMax,
            indexBufferBytes,
            vertexBufferBytesMin + indexBufferBytes,
            vertexBufferBytesMax + indexBufferBytes,
            packageEstimates);
    }

    private static DatasetPackageGeometryVramEstimate CreatePackageGeometryVramEstimate(
        string packageName,
        GeometryVramAccumulator accumulator)
    {
        long vertexBufferBytesMin = accumulator.PositionCount * GeometryVertexBytesMin;
        long vertexBufferBytesMax = accumulator.PositionCount * GeometryVertexBytesMax;
        long indexBufferBytes = accumulator.TriangleCount * 3 * GeometryIndexBytes;

        return new DatasetPackageGeometryVramEstimate(
            packageName,
            accumulator.PositionCount,
            accumulator.TriangleCount,
            vertexBufferBytesMin,
            vertexBufferBytesMax,
            indexBufferBytes,
            vertexBufferBytesMin + indexBufferBytes,
            vertexBufferBytesMax + indexBufferBytes);
    }

    private static GeometryVramAccumulator GetOrCreateGeometryAccumulator(
        Dictionary<string, GeometryVramAccumulator> geometryByPackage,
        string packageName)
    {
        if (!geometryByPackage.TryGetValue(packageName, out GeometryVramAccumulator? accumulator))
        {
            accumulator = new GeometryVramAccumulator();
            geometryByPackage[packageName] = accumulator;
        }

        return accumulator;
    }

    private static string? ResolveRepresentativeMeshCode(LocalCityGmlDatasetSourceFileCandidate candidate)
    {
        return candidate.FileMeshCodes
            .Concat(candidate.DirectoryMeshCodes)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(static meshCode => meshCode.Length)
            .ThenBy(static meshCode => meshCode, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}

public sealed record DatasetSearchResult(
    IReadOnlyList<DatasetSearchEntry> SourceFiles,
    IReadOnlyList<string> SelectedMeshCodes);

public sealed record DatasetSearchEntry(
    string RelativePath,
    string PackageName,
    string MatchedMeshCode,
    bool RequiresMeshAreaFilter);

public sealed record DatasetStatsResult(
    int RecognizedSourceFileCount,
    IReadOnlyDictionary<string, int> PackageCounts,
    IReadOnlyDictionary<string, int> MeshCodeCounts,
    IReadOnlyDictionary<int, int> LodCoverageCounts,
    int FilesWithoutDetectedLod,
    DatasetArchiveVramEstimate ArchiveVramEstimate);

public sealed record DatasetArchiveVramEstimate(
    DatasetTextureVramEstimate RendererTextureVram,
    DatasetGeometryVramEstimate RendererGeometryVram,
    long RendererTotalBytesMin,
    long RendererTotalBytesMax);

public sealed record DatasetTextureVramEstimate(
    int ReferencedTextureCount,
    int ResolvedTextureFileCount,
    int MissingTextureReferenceCount,
    long Bc1TextureCount,
    long Bc3TextureCount,
    long Bc1Bytes,
    long Bc3Bytes,
    long RendererTotalBytes,
    long Rgba32PayloadBytes,
    IReadOnlyDictionary<string, DatasetPackageTextureVramEstimate> PackageEstimates);

public sealed record DatasetPackageTextureVramEstimate(
    int ResolvedTextureFileCount,
    int MissingTextureReferenceCount,
    long Bc1TextureCount,
    long Bc3TextureCount,
    long Bc1Bytes,
    long Bc3Bytes,
    long RendererTotalBytes,
    long Rgba32PayloadBytes);

public sealed record DatasetGeometryVramEstimate(
    long PositionCount,
    long TriangleCount,
    long VertexBufferBytesMin,
    long VertexBufferBytesMax,
    long IndexBufferBytes,
    long RendererBytesMin,
    long RendererBytesMax,
    IReadOnlyDictionary<string, DatasetPackageGeometryVramEstimate> PackageEstimates);

public sealed record DatasetPackageGeometryVramEstimate(
    string PackageName,
    long PositionCount,
    long TriangleCount,
    long VertexBufferBytesMin,
    long VertexBufferBytesMax,
    long IndexBufferBytes,
    long RendererBytesMin,
    long RendererBytesMax);

internal sealed record DatasetSourceFileStats(
    IReadOnlyList<int> LodLevels,
    IReadOnlyList<string> ResolvedTexturePaths,
    DatasetPackageGeometryVramEstimate Geometry);

internal sealed record DatasetTextureVramEntry(
    string RelativePath,
    int Width,
    int Height,
    bool HasEffectiveAlpha,
    long RendererBytes,
    long Rgba32PayloadBytes);

internal sealed class TextureVramAccumulator
{
    public int ReferencedTextureCount { get; set; }

    public int ResolvedTextureFileCount { get; set; }

    public int MissingTextureReferenceCount { get; set; }

    public long Bc1TextureCount { get; set; }

    public long Bc3TextureCount { get; set; }

    public long Bc1Bytes { get; set; }

    public long Bc3Bytes { get; set; }

    public long RendererTotalBytes { get; set; }

    public long Rgba32PayloadBytes { get; set; }

    public void Add(DatasetTextureVramEntry entry)
    {
        ReferencedTextureCount++;
        ResolvedTextureFileCount++;
        if (entry.HasEffectiveAlpha)
        {
            Bc3TextureCount++;
            Bc3Bytes += entry.RendererBytes;
        }
        else
        {
            Bc1TextureCount++;
            Bc1Bytes += entry.RendererBytes;
        }

        RendererTotalBytes += entry.RendererBytes;
        Rgba32PayloadBytes += entry.Rgba32PayloadBytes;
    }

    public DatasetPackageTextureVramEstimate ToEstimate()
    {
        return new DatasetPackageTextureVramEstimate(
            ResolvedTextureFileCount,
            MissingTextureReferenceCount,
            Bc1TextureCount,
            Bc3TextureCount,
            Bc1Bytes,
            Bc3Bytes,
            RendererTotalBytes,
            Rgba32PayloadBytes);
    }
}

internal sealed class GeometryVramAccumulator
{
    public long PositionCount { get; set; }

    public long TriangleCount { get; set; }
}
