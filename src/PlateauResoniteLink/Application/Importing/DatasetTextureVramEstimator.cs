using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Application.Importing;

internal static class DatasetTextureVramEstimator
{
    private const int BcBlockPixelSize = 4;
    private const int Bc1BlockBytes = 8;
    private const int Bc3BlockBytes = 16;
    internal static async Task<DatasetTextureVramEstimate> EstimateAsync(
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
}

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
