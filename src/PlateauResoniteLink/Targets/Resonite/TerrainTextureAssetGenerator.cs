using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface ITerrainTextureAssetGenerator
{
    Task<GeneratedTerrainTexture> EnsureTextureAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken);
}

internal sealed record GeneratedTerrainTexture(
    ResoniteRawTextureImport TextureImport,
    TextureUvRect OccupiedUvRect,
    TerrainTextureSource? UsedSource = null,
    IReadOnlyList<TerrainTextureSource>? UsedSources = null)
{
    public GeneratedTerrainTexture(
        ResoniteRawTextureImport textureImport,
        ResoniteFloat2 canvasScale,
        ResoniteFloat2 canvasOffset,
        TerrainTextureSource? usedSource = null,
        IReadOnlyList<TerrainTextureSource>? usedSources = null)
        : this(
            textureImport,
            TextureUvRect.FromScaleOffsetValue(
                new ScalarPair(canvasScale.X, canvasScale.Y),
                new ScalarPair(canvasOffset.X, canvasOffset.Y)),
            usedSource,
            usedSources)
    {
    }
}

internal sealed class TerrainTextureAssetGenerator(
    HttpClient? httpClient = null,
    string? persistentCacheRoot = null,
    bool disablePersistentCache = false) : ITerrainTextureAssetGenerator
{
    private const int MaxTileDownloadAttempts = 4;
    // Approximate dry brown soil tone (Munsell 10YR 5/3 family) for uncovered DEM texels.
    internal static readonly Rgba32 DefaultDemGroundFillColor = new(181, 176, 166, byte.MaxValue);

    private readonly HttpClient httpClient = httpClient ?? new HttpClient();
    private readonly AsyncCompletedResultCache<TerrainTextureOverlay, CachedTerrainTexture> cachedTextures = new();
    private readonly PersistentTerrainTileCache? persistentTileCache = disablePersistentCache
        ? null
        : new PersistentTerrainTileCache(persistentCacheRoot);

    public async Task<GeneratedTerrainTexture> EnsureTextureAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureOverlay);

        CachedTerrainTexture cachedTexture = await cachedTextures.GetOrCreateAsync(
            terrainTextureOverlay,
            ct => CreateTextureAsync(terrainTextureOverlay, ct),
            cancellationToken);
        return cachedTexture.GeneratedTexture;
    }

    private async Task<CachedTerrainTexture> CreateTextureAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken)
    {
        Image<Rgba32>? composedTexture = null;
        TextureUvRect? composedOccupiedUvRect = null;
        List<TerrainTextureSource> usedSources = [];
        TerrainTextureSource? usedSource = null;

        for (int sourceIndex = 0; sourceIndex < terrainTextureOverlay.Sources.Count; sourceIndex++)
        {
            TerrainTextureSource terrainTextureSource = terrainTextureOverlay.Sources[sourceIndex];
            TerrainTextureSourceImage? sourceImage = terrainTextureSource switch
            {
                TerrainTextureTileSource tileSource => await TryCreateTextureFromTileSourceAsync(
                    terrainTextureOverlay,
                    tileSource,
                    cancellationToken),
                TerrainTextureGeoReferencedRasterSource rasterSource => await TryCreateTextureFromGeoReferencedRasterSourceAsync(
                    terrainTextureOverlay,
                    rasterSource,
                    cancellationToken),
                _ => null,
            };
            if (sourceImage is null)
            {
                continue;
            }

            using (sourceImage)
            {
                Image<Rgba32> image = sourceImage.Image;
                if (!HasRenderablePixels(image))
                {
                    continue;
                }

                if (composedTexture is null)
                {
                    composedTexture = image.Clone();
                    composedOccupiedUvRect = sourceImage.OccupiedUvRect;
                    usedSource = terrainTextureSource;
                    usedSources.Add(terrainTextureSource);
                }
                else
                {
                    using Image<Rgba32> resizedImage = ResizeSourceImage(image, composedTexture.Width, composedTexture.Height);
                    if (FillTransparentPixels(composedTexture, resizedImage))
                    {
                        usedSource = terrainTextureSource;
                        usedSources.Add(terrainTextureSource);
                    }
                }
                if (!HasTransparentPixels(composedTexture))
                {
                    break;
                }
            }
        }

        if (composedTexture is null)
        {
            throw new HttpRequestException(
                $"Terrain texture generation failed for sources [{DescribeTerrainTextureSources(terrainTextureOverlay.Sources)}].");
        }

        using (composedTexture)
        {
            TerrainTextureSource terrainTextureSource = usedSource ?? terrainTextureOverlay.PrimarySource;
            GeneratedTerrainTexture generatedTexture = CreateGeneratedTexture(
                composedTexture,
                terrainTextureOverlay.MaxTextureSize,
                terrainTextureSource,
                usedSources,
                composedOccupiedUvRect);
            return new CachedTerrainTexture(generatedTexture, terrainTextureSource);
        }
    }

    private static ExpandedTileCrop CreateExpandedTileCrop(
        TerrainTextureLayoutPlan layoutPlan,
        int zoomLevel,
        int maxTextureSize)
    {
        int canvasWidth = RoundUpToPowerOfTwo(layoutPlan.CropWidth);
        int canvasHeight = RoundUpToPowerOfTwo(layoutPlan.CropHeight);
        if (canvasWidth > maxTextureSize || canvasHeight > maxTextureSize)
        {
            return ExpandedTileCrop.FromLayout(layoutPlan);
        }

        int occupiedLeft = (canvasWidth - layoutPlan.CropWidth) / 2;
        int occupiedTop = (canvasHeight - layoutPlan.CropHeight) / 2;
        int layoutGlobalLeft = (layoutPlan.MinTileX * WebMercatorTileMath.TileSizePixels) + layoutPlan.CropLeft;
        int layoutGlobalTop = (layoutPlan.MinTileY * WebMercatorTileMath.TileSizePixels) + layoutPlan.CropTop;
        int expandedGlobalLeft = layoutGlobalLeft - occupiedLeft;
        int expandedGlobalTop = layoutGlobalTop - occupiedTop;
        int expandedGlobalRight = expandedGlobalLeft + canvasWidth;
        int expandedGlobalBottom = expandedGlobalTop + canvasHeight;
        int maxTileIndex = (1 << zoomLevel) - 1;
        int minTileX = Math.Clamp((int)Math.Floor(expandedGlobalLeft / (double)WebMercatorTileMath.TileSizePixels), 0, maxTileIndex);
        int maxTileX = Math.Clamp((int)Math.Floor((expandedGlobalRight - 1) / (double)WebMercatorTileMath.TileSizePixels), 0, maxTileIndex);
        int minTileY = Math.Clamp((int)Math.Floor(expandedGlobalTop / (double)WebMercatorTileMath.TileSizePixels), 0, maxTileIndex);
        int maxTileY = Math.Clamp((int)Math.Floor((expandedGlobalBottom - 1) / (double)WebMercatorTileMath.TileSizePixels), 0, maxTileIndex);
        int stitchedWidth = (maxTileX - minTileX + 1) * WebMercatorTileMath.TileSizePixels;
        int stitchedHeight = (maxTileY - minTileY + 1) * WebMercatorTileMath.TileSizePixels;
        int cropLeft = Math.Clamp(expandedGlobalLeft - (minTileX * WebMercatorTileMath.TileSizePixels), 0, Math.Max(0, stitchedWidth - canvasWidth));
        int cropTop = Math.Clamp(expandedGlobalTop - (minTileY * WebMercatorTileMath.TileSizePixels), 0, Math.Max(0, stitchedHeight - canvasHeight));
        int actualCropWidth = Math.Min(canvasWidth, stitchedWidth - cropLeft);
        int actualCropHeight = Math.Min(canvasHeight, stitchedHeight - cropTop);
        if (actualCropWidth <= 0 || actualCropHeight <= 0)
        {
            return ExpandedTileCrop.FromLayout(layoutPlan);
        }

        int occupiedX = Math.Clamp(
            layoutGlobalLeft - ((minTileX * WebMercatorTileMath.TileSizePixels) + cropLeft),
            0,
            actualCropWidth - 1);
        int occupiedY = Math.Clamp(
            layoutGlobalTop - ((minTileY * WebMercatorTileMath.TileSizePixels) + cropTop),
            0,
            actualCropHeight - 1);
        TextureUvRect occupiedUvRect = TextureUvRect.FromTopLeftPixelRect(
            occupiedX,
            occupiedY,
            Math.Min(layoutPlan.CropWidth, actualCropWidth - occupiedX),
            Math.Min(layoutPlan.CropHeight, actualCropHeight - occupiedY),
            actualCropWidth,
            actualCropHeight);
        return new ExpandedTileCrop(
            minTileX,
            maxTileX,
            minTileY,
            maxTileY,
            stitchedWidth,
            stitchedHeight,
            cropLeft,
            cropTop,
            actualCropWidth,
            actualCropHeight,
            occupiedUvRect);
    }

    private async Task<TerrainTextureSourceImage?> TryCreateTextureFromTileSourceAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        TerrainTextureTileSource tileSource,
        CancellationToken cancellationToken)
    {
        TerrainTextureLayoutPlan layoutPlan = TerrainTextureLayoutPlanner.Create(
            terrainTextureOverlay.GeographicBounds,
            tileSource.ZoomLevel);
        ExpandedTileCrop tileCrop = CreateExpandedTileCrop(layoutPlan, tileSource.ZoomLevel, terrainTextureOverlay.MaxTextureSize);
        using Image<Rgba32> stitchedImage = new(tileCrop.StitchedWidth, tileCrop.StitchedHeight);
        bool anyTileRendered = false;
        for (int tileY = tileCrop.MinTileY; tileY <= tileCrop.MaxTileY; tileY++)
        {
            for (int tileX = tileCrop.MinTileX; tileX <= tileCrop.MaxTileX; tileX++)
            {
                Image<Rgba32>? tileImage = await TryDownloadTileAsync(
                    tileSource,
                    tileX,
                    tileY,
                    cancellationToken);
                if (tileImage is null)
                {
                    continue;
                }

                using (tileImage)
                {
                    anyTileRendered = true;
                    stitchedImage.Mutate(context => context.DrawImage(
                        tileImage,
                        new Point(
                            (tileX - tileCrop.MinTileX) * WebMercatorTileMath.TileSizePixels,
                            (tileY - tileCrop.MinTileY) * WebMercatorTileMath.TileSizePixels),
                        1.0f));
                }
            }
        }

        if (!anyTileRendered)
        {
            return null;
        }

        return new TerrainTextureSourceImage(
            stitchedImage.Clone(context => context.Crop(new Rectangle(
                tileCrop.CropLeft,
                tileCrop.CropTop,
                tileCrop.CropWidth,
                tileCrop.CropHeight))),
            tileCrop.OccupiedUvRect);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The returned source image owns and disposes the cropped raster image.")]
    private static async Task<TerrainTextureSourceImage?> TryCreateTextureFromGeoReferencedRasterSourceAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        TerrainTextureGeoReferencedRasterSource rasterSource,
        CancellationToken cancellationToken)
    {
        string sourcePath = Path.GetFullPath(rasterSource.SourcePath);
        GeoReferencedRasterMetadata? metadata = rasterSource.Metadata
            ?? await TerrainTextureGeoReferencedRasterMetadataReader.TryReadMetadataAsync(sourcePath, cancellationToken);
        if (metadata is null || !metadata.IsUsable)
        {
            return null;
        }

        try
        {
            using Image<Rgba32> sourceImage = await Image.LoadAsync<Rgba32>(sourcePath, cancellationToken);
            Image<Rgba32>? cropped = TerrainTextureGeoReferencedRasterCropper.TryCrop(
                sourceImage,
                metadata,
                terrainTextureOverlay.GeographicBounds);
            return cropped is null
                ? null
                : new TerrainTextureSourceImage(cropped, null);
        }
        catch (UnknownImageFormatException)
        {
            return null;
        }
        catch (InvalidImageContentException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static GeneratedTerrainTexture CreateGeneratedTexture(
        Image<Rgba32> image,
        int maxTextureSize,
        TerrainTextureSource usedSource,
        List<TerrainTextureSource> usedSources,
        TextureUvRect? sourceOccupiedUvRect)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTextureSize);
        using Image<Rgba32> opaqueImage = CreateOpaqueGroundImage(image);

        if (TryCreatePowerOfTwoCanvasTexture(opaqueImage, maxTextureSize, usedSource, usedSources, sourceOccupiedUvRect, out GeneratedTerrainTexture? generatedTexture))
        {
            return generatedTexture!;
        }

        int fallbackMaxTextureSize = RoundDownToPowerOfTwo(maxTextureSize);
        using Image<Rgba32> resizedImage = ResizeToMaxTextureSize(opaqueImage, fallbackMaxTextureSize);
        if (TryCreatePowerOfTwoCanvasTexture(resizedImage, fallbackMaxTextureSize, usedSource, usedSources, null, out generatedTexture))
        {
            return generatedTexture!;
        }

        throw new InvalidOperationException(
            $"Terrain texture fallback failed to fit into a power-of-two canvas within maxTextureSize={maxTextureSize}.");
    }

    private static bool TryCreatePowerOfTwoCanvasTexture(
        Image<Rgba32> image,
        int maxTextureSize,
        TerrainTextureSource usedSource,
        IReadOnlyList<TerrainTextureSource> usedSources,
        TextureUvRect? sourceOccupiedUvRect,
        out GeneratedTerrainTexture? generatedTexture)
    {
        int canvasWidth = RoundUpToPowerOfTwo(image.Width);
        int canvasHeight = RoundUpToPowerOfTwo(image.Height);
        if (canvasWidth > maxTextureSize || canvasHeight > maxTextureSize)
        {
            generatedTexture = null;
            return false;
        }

        using Image<Rgba32> canvasImage = new(canvasWidth, canvasHeight, DefaultDemGroundFillColor);
        int drawOffsetX = (canvasWidth - image.Width) / 2;
        int drawOffsetY = (canvasHeight - image.Height) / 2;
        canvasImage.Mutate(context => context.DrawImage(
            image,
            new Point(drawOffsetX, drawOffsetY),
            1.0f));
        TextureUvRect occupiedUvRect = sourceOccupiedUvRect is { } sourceRect
            ? CreateOccupiedUvRect(
                (int)Math.Round(drawOffsetX + (sourceRect.MinU * image.Width), MidpointRounding.AwayFromZero),
                (int)Math.Round(drawOffsetY + ((1.0 - sourceRect.MaxV) * image.Height), MidpointRounding.AwayFromZero),
                (int)Math.Round(sourceRect.Width * image.Width, MidpointRounding.AwayFromZero),
                (int)Math.Round(sourceRect.Height * image.Height, MidpointRounding.AwayFromZero),
                canvasWidth,
                canvasHeight)
            : TextureUvRect.FromTopLeftPixelRect(
                drawOffsetX,
                drawOffsetY,
                image.Width,
                image.Height,
                canvasWidth,
                canvasHeight);
        generatedTexture = new GeneratedTerrainTexture(
            CreateRawTextureImport(canvasImage),
            occupiedUvRect,
            usedSource,
            usedSources.Distinct().ToArray());
        return true;
    }

    private static TextureUvRect CreateOccupiedUvRect(
        int x,
        int y,
        int width,
        int height,
        int canvasWidth,
        int canvasHeight)
    {
        int clampedX = Math.Clamp(x, 0, canvasWidth - 1);
        int clampedY = Math.Clamp(y, 0, canvasHeight - 1);
        return TextureUvRect.FromTopLeftPixelRect(
            clampedX,
            clampedY,
            Math.Min(width, canvasWidth - clampedX),
            Math.Min(height, canvasHeight - clampedY),
            canvasWidth,
            canvasHeight);
    }

    private static Image<Rgba32> CreateOpaqueGroundImage(Image<Rgba32> image)
    {
        Image<Rgba32> opaqueImage = new(image.Width, image.Height, DefaultDemGroundFillColor);
        opaqueImage.Mutate(context => context.DrawImage(image, new Point(0, 0), 1.0f));
        return opaqueImage;
    }

    private static Image<Rgba32> ResizeToMaxTextureSize(Image<Rgba32> image, int maxTextureSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTextureSize);

        if (image.Width <= maxTextureSize && image.Height <= maxTextureSize)
        {
            return image.Clone();
        }

        return image.Clone(context => context.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(maxTextureSize, maxTextureSize),
            Sampler = KnownResamplers.Lanczos3,
        }));
    }

    private static int RoundUpToPowerOfTwo(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);

        int rounded = 1;
        while (rounded < value)
        {
            rounded <<= 1;
        }

        return rounded;
    }

    private static int RoundDownToPowerOfTwo(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);

        int rounded = 1;
        while ((rounded << 1) > 0 && (rounded << 1) <= value)
        {
            rounded <<= 1;
        }

        return rounded;
    }

    private static Image<Rgba32> ResizeSourceImage(Image<Rgba32> image, int width, int height)
    {
        return image.Width == width && image.Height == height
            ? image.Clone()
            : image.Clone(context => context.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Stretch,
                Size = new Size(width, height),
                Sampler = KnownResamplers.Lanczos3,
            }));
    }

    private static bool HasRenderablePixels(Image<Rgba32> image)
    {
        bool hasRenderablePixels = false;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height && !hasRenderablePixels; y++)
            {
                ReadOnlySpan<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (row[x].A > 0)
                    {
                        hasRenderablePixels = true;
                        break;
                    }
                }
            }
        });

        return hasRenderablePixels;
    }

    private static bool HasTransparentPixels(Image<Rgba32> image)
    {
        bool hasTransparentPixels = false;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height && !hasTransparentPixels; y++)
            {
                ReadOnlySpan<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (row[x].A == 0)
                    {
                        hasTransparentPixels = true;
                        break;
                    }
                }
            }
        });

        return hasTransparentPixels;
    }

    private static bool FillTransparentPixels(Image<Rgba32> destination, Image<Rgba32> fallback)
    {
        bool filledAny = false;
        for (int y = 0; y < destination.Height; y++)
        {
            for (int x = 0; x < destination.Width; x++)
            {
                if (destination[x, y].A > 0)
                {
                    continue;
                }

                Rgba32 fallbackPixel = fallback[x, y];
                if (fallbackPixel.A > 0)
                {
                    destination[x, y] = fallbackPixel;
                    filledAny = true;
                }
            }
        }

        return filledAny;
    }

    private static string DescribeTerrainTextureSources(IEnumerable<TerrainTextureSource> sources)
    {
        return string.Join(
            ", ",
            sources.Select(static source => source switch
            {
                TerrainTextureTileSource tileSource => $"tile:{tileSource.ZoomLevel}:{tileSource.UrlTemplate}",
                TerrainTextureGeoReferencedRasterSource rasterSource => $"georaster:{rasterSource.SourcePath}",
                _ => source.GetType().Name,
            }));
    }

    private async Task<Image<Rgba32>?> TryDownloadTileAsync(
        TerrainTextureTileSource tileSource,
        int tileX,
        int tileY,
        CancellationToken cancellationToken)
    {
        string tileUrl = WebMercatorTileMath.FormatTileUrl(tileSource.UrlTemplate, tileSource.ZoomLevel, tileX, tileY);
        if (persistentTileCache is not null)
        {
            byte[]? cachedBytes = await persistentTileCache.TryReadTileBytesAsync(
                tileSource.UrlTemplate,
                tileSource.ZoomLevel,
                tileX,
                tileY,
                cancellationToken);
            if (cachedBytes is not null)
            {
                try
                {
                    return await LoadTileImageAsync(cachedBytes, cancellationToken);
                }
                catch (UnknownImageFormatException)
                {
                }
                catch (InvalidImageContentException)
                {
                }
            }
        }

        for (int attempt = 1; attempt <= MaxTileDownloadAttempts; attempt++)
        {
            try
            {
                using HttpResponseMessage response = await httpClient.GetAsync(
                    tileUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    if (attempt < MaxTileDownloadAttempts && IsTransientStatusCode((int)response.StatusCode))
                    {
                        await Task.Delay(GetRetryDelay(attempt), cancellationToken);
                        continue;
                    }

                    return null;
                }

                byte[] encodedBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (persistentTileCache is not null)
                {
                    await TryWritePersistentTileBytesAsync(
                        tileSource.UrlTemplate,
                        tileSource.ZoomLevel,
                        tileX,
                        tileY,
                        encodedBytes,
                        cancellationToken);
                }

                return await LoadTileImageAsync(encodedBytes, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < MaxTileDownloadAttempts)
            {
                await Task.Delay(GetRetryDelay(attempt), cancellationToken);
            }
        }

        return null;
    }

    private async Task TryWritePersistentTileBytesAsync(
        string urlTemplate,
        int zoomLevel,
        int tileX,
        int tileY,
        byte[] encodedBytes,
        CancellationToken cancellationToken)
    {
        if (persistentTileCache is null)
        {
            return;
        }

        try
        {
            await persistentTileCache.WriteTileBytesAsync(
                urlTemplate,
                zoomLevel,
                tileX,
                tileY,
                encodedBytes,
                cancellationToken);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static async Task<Image<Rgba32>> LoadTileImageAsync(
        byte[] encodedBytes,
        CancellationToken cancellationToken)
    {
        using MemoryStream stream = new(encodedBytes, writable: false);
        return await Image.LoadAsync<Rgba32>(stream, cancellationToken);
    }

    private static ResoniteRawTextureImport CreateRawTextureImport(Image<Rgba32> image)
    {
        byte[] rawBytes = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(rawBytes);
        return new ResoniteRawTextureImport(
            image.Width,
            image.Height,
            "sRGB",
            rawBytes);
    }

    private sealed record CachedTerrainTexture(
        GeneratedTerrainTexture GeneratedTexture,
        TerrainTextureSource UsedSource);

    private sealed record TerrainTextureSourceImage(
        Image<Rgba32> Image,
        TextureUvRect? OccupiedUvRect) : IDisposable
    {
        public void Dispose()
        {
            Image.Dispose();
        }
    }

    private sealed record ExpandedTileCrop(
        int MinTileX,
        int MaxTileX,
        int MinTileY,
        int MaxTileY,
        int StitchedWidth,
        int StitchedHeight,
        int CropLeft,
        int CropTop,
        int CropWidth,
        int CropHeight,
        TextureUvRect? OccupiedUvRect)
    {
        public static ExpandedTileCrop FromLayout(TerrainTextureLayoutPlan layoutPlan)
        {
            return new ExpandedTileCrop(
                layoutPlan.MinTileX,
                layoutPlan.MaxTileX,
                layoutPlan.MinTileY,
                layoutPlan.MaxTileY,
                layoutPlan.StitchedWidth,
                layoutPlan.StitchedHeight,
                layoutPlan.CropLeft,
                layoutPlan.CropTop,
                layoutPlan.CropWidth,
                layoutPlan.CropHeight,
                null);
        }
    }

    private static bool IsTransientStatusCode(int statusCode)
    {
        return statusCode == 408
            || statusCode == 425
            || statusCode == 429
            || statusCode >= 500;
    }

    private static TimeSpan GetRetryDelay(int attempt)
    {
        return TimeSpan.FromMilliseconds(250 * attempt);
    }
}

internal sealed class PersistentTerrainTileCache
{
    private readonly string cacheRoot;

    public PersistentTerrainTileCache(string? cacheRoot)
    {
        this.cacheRoot = Path.GetFullPath(cacheRoot ?? GetDefaultCacheRoot());
    }

    public async Task<byte[]?> TryReadTileBytesAsync(
        string urlTemplate,
        int zoomLevel,
        int tileX,
        int tileY,
        CancellationToken cancellationToken)
    {
        string cachePath = GetCachePath(urlTemplate, zoomLevel, tileX, tileY);
        if (!File.Exists(cachePath))
        {
            return null;
        }

        try
        {
            return await File.ReadAllBytesAsync(cachePath, cancellationToken);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task WriteTileBytesAsync(
        string urlTemplate,
        int zoomLevel,
        int tileX,
        int tileY,
        byte[] encodedBytes,
        CancellationToken cancellationToken)
    {
        string cachePath = GetCachePath(urlTemplate, zoomLevel, tileX, tileY);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

        string temporaryPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, encodedBytes, cancellationToken);
            File.Move(temporaryPath, cachePath, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }

    private string GetCachePath(string urlTemplate, int zoomLevel, int tileX, int tileY)
    {
        string templateDigest = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(urlTemplate)))
            .ToLowerInvariant();
        return Path.Combine(
            cacheRoot,
            templateDigest,
            zoomLevel.ToString(System.Globalization.CultureInfo.InvariantCulture),
            tileX.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"{tileY.ToString(System.Globalization.CultureInfo.InvariantCulture)}.tile");
    }

    private static string GetDefaultCacheRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PlateauResoniteLink",
            "terrain-tile-cache");
    }
}
