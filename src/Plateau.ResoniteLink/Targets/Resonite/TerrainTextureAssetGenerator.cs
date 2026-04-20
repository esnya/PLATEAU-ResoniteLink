using System.Security.Cryptography;
using System.Text;

using Plateau.ResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Plateau.ResoniteLink.Targets.Resonite;

internal interface ITerrainTextureAssetGenerator
{
    Task<TerrainTextureGenerationResult> EnsureTextureWithSourceAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken);

    Task<GeneratedTerrainTexture> EnsureTextureAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken)
    {
        return EnsureTextureWithSourceAsync(terrainTextureOverlay, cancellationToken)
            .ContinueWith(
                static task => task.GetAwaiter().GetResult().GeneratedTexture,
                cancellationToken,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }
}

internal sealed record GeneratedTerrainTexture(
    ResoniteRawTextureImport TextureImport,
    ResoniteFloat2 CanvasScale,
    ResoniteFloat2 CanvasOffset);

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
        return (await EnsureTextureWithSourceAsync(terrainTextureOverlay, cancellationToken)).GeneratedTexture;
    }

    public async Task<TerrainTextureGenerationResult> EnsureTextureWithSourceAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureOverlay);

        CachedTerrainTexture cachedTexture = await cachedTextures.GetOrCreateAsync(
            terrainTextureOverlay,
            ct => CreateTextureAsync(terrainTextureOverlay, ct),
            cancellationToken);
        return new TerrainTextureGenerationResult(
            cachedTexture.GeneratedTexture,
            cachedTexture.UsedSource,
            cachedTexture.AdditionalUsedSources,
            cachedTexture.UsesGsiFallbackLicense);
    }

    private async Task<CachedTerrainTexture> CreateTextureAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken)
    {
        TerrainTextureCanvasLayout? canvasLayout = null;
        Image<Rgba32>? canvas = null;
        bool[]? filledPixels = null;
        int filledPixelCount = 0;
        List<TerrainTextureSource> usedSources = [];
        for (int sourceIndex = 0; sourceIndex < terrainTextureOverlay.Sources.Count; sourceIndex++)
        {
            TerrainTextureSource source = terrainTextureOverlay.Sources[sourceIndex];
            RenderedTerrainTextureSource? renderedSource = source switch
            {
                TerrainTextureTileSource tileSource => await TryRenderTextureFromTileSourceAsync(
                    terrainTextureOverlay,
                    tileSource,
                    cancellationToken),
                TerrainTextureGeoReferencedRasterSource rasterSource => await TryRenderTextureFromGeoReferencedRasterSourceAsync(
                    terrainTextureOverlay,
                    rasterSource,
                    cancellationToken),
                _ => null,
            };

            if (renderedSource is null)
            {
                continue;
            }

            using (renderedSource)
            {
                canvasLayout ??= TerrainTextureCanvasLayout.Create(
                    terrainTextureOverlay.GeographicBounds,
                    renderedSource);
                canvas ??= new Image<Rgba32>(canvasLayout.Width, canvasLayout.Height);
                filledPixels ??= new bool[canvas.Width * canvas.Height];
                int newlyFilledPixels = CompositeRenderedSource(
                    canvas,
                    filledPixels,
                    canvasLayout,
                    renderedSource);
                if (newlyFilledPixels > 0)
                {
                    usedSources.Add(renderedSource.Source);
                    filledPixelCount += newlyFilledPixels;
                    if (filledPixelCount >= filledPixels.Length)
                    {
                        break;
                    }
                }
            }
        }

        if (canvas is null || filledPixels is null || usedSources.Count == 0)
        {
            throw new HttpRequestException(
                $"Terrain texture generation failed for '{terrainTextureOverlay.SourceIdentityKey}'.");
        }

        using (canvas)
        {
            FillUncoveredPixels(canvas, filledPixels);

            GeneratedTerrainTexture generatedTexture = CreateGeneratedTexture(
                canvas,
                terrainTextureOverlay.MaxTextureSize,
                CreateOverlayIdentity(terrainTextureOverlay, usedSources));
            return new CachedTerrainTexture(
                generatedTexture,
                usedSources[0],
                usedSources.Skip(1).ToArray(),
                usedSources.Any(IsGsiFallbackSource));
        }
    }

    private async Task<RenderedTerrainTextureSource?> TryRenderTextureFromTileSourceAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        TerrainTextureTileSource tileSource,
        CancellationToken cancellationToken)
    {
        TerrainTextureLayoutPlan layoutPlan = TerrainTextureLayoutPlanner.Create(
            terrainTextureOverlay.GeographicBounds,
            tileSource.ZoomLevel);
        using Image<Rgba32> stitchedImage = new(layoutPlan.StitchedWidth, layoutPlan.StitchedHeight);

        for (int tileY = layoutPlan.MinTileY; tileY <= layoutPlan.MaxTileY; tileY++)
        {
            for (int tileX = layoutPlan.MinTileX; tileX <= layoutPlan.MaxTileX; tileX++)
            {
                Image<Rgba32>? tileImage = await TryDownloadTileAsync(
                    tileSource,
                    tileX,
                    tileY,
                    cancellationToken);
                if (tileImage is null)
                {
                    return null;
                }

                using (tileImage)
                {
                    stitchedImage.Mutate(context => context.DrawImage(
                        tileImage,
                        new Point(
                            (tileX - layoutPlan.MinTileX) * WebMercatorTileMath.TileSizePixels,
                            (tileY - layoutPlan.MinTileY) * WebMercatorTileMath.TileSizePixels),
                        1.0f));
                }
            }
        }

        return new RenderedTerrainTextureSource(
            stitchedImage.Clone(context => context.Crop(new Rectangle(
                layoutPlan.CropLeft,
                layoutPlan.CropTop,
                layoutPlan.CropWidth,
                layoutPlan.CropHeight))),
            tileSource,
            terrainTextureOverlay.GeographicBounds,
            layoutPlan.CropWidth,
            layoutPlan.CropHeight);
    }

    private static async Task<RenderedTerrainTextureSource?> TryRenderTextureFromGeoReferencedRasterSourceAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        TerrainTextureGeoReferencedRasterSource rasterSource,
        CancellationToken cancellationToken)
    {
        string sourcePath = Path.GetFullPath(rasterSource.SourcePath);
        GeoReferencedRasterMetadata? metadata = rasterSource.Metadata
            ?? await global::Plateau.ResoniteLink.Application.Importing.TerrainTextureGeoReferencedRasterMetadataReader.TryReadMetadataAsync(sourcePath, cancellationToken);
        if (metadata is null || !metadata.IsUsable)
        {
            return null;
        }

        try
        {
            using Image<Rgba32> sourceImage = await Image.LoadAsync<Rgba32>(sourcePath, cancellationToken);
            global::Plateau.ResoniteLink.Application.Importing.GeoReferencedRasterCropResult? crop =
                global::Plateau.ResoniteLink.Application.Importing.TerrainTextureGeoReferencedRasterCropper.TryCrop(
                sourceImage,
                metadata,
                terrainTextureOverlay.GeographicBounds);
            if (crop is null)
            {
                return null;
            }

            return new RenderedTerrainTextureSource(
                crop.Image,
                rasterSource,
                crop.CoverageBounds,
                null,
                null);
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

    private static int CompositeRenderedSource(
        Image<Rgba32> canvas,
        bool[] filledPixels,
        TerrainTextureCanvasLayout canvasLayout,
        RenderedTerrainTextureSource renderedSource)
    {
        Rectangle targetRectangle = canvasLayout.GetTargetRectangle(renderedSource.CoverageBounds);
        if (targetRectangle.Width <= 0 || targetRectangle.Height <= 0)
        {
            return 0;
        }

        Image<Rgba32> sourceImage = renderedSource.Image;
        Image<Rgba32>? resizedImage = null;
        if (sourceImage.Width != targetRectangle.Width || sourceImage.Height != targetRectangle.Height)
        {
            resizedImage = sourceImage.Clone(context => context.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Stretch,
                Size = targetRectangle.Size,
                Sampler = KnownResamplers.Lanczos3,
            }));
            sourceImage = resizedImage;
        }

        try
        {
            int newlyFilledPixels = 0;
            for (int sourceY = 0; sourceY < sourceImage.Height; sourceY++)
            {
                int pixelBaseIndex = (targetRectangle.Y + sourceY) * canvas.Width;
                for (int sourceX = 0; sourceX < sourceImage.Width; sourceX++)
                {
                    int targetX = targetRectangle.X + sourceX;
                    int filledIndex = pixelBaseIndex + targetX;
                    if (filledPixels[filledIndex])
                    {
                        continue;
                    }

                    Rgba32 sourcePixel = sourceImage[sourceX, sourceY];
                    if (sourcePixel.A == 0)
                    {
                        continue;
                    }

                    sourcePixel.A = byte.MaxValue;
                    canvas[targetX, targetRectangle.Y + sourceY] = sourcePixel;
                    filledPixels[filledIndex] = true;
                    newlyFilledPixels++;
                }
            }

            return newlyFilledPixels;
        }
        finally
        {
            resizedImage?.Dispose();
        }
    }

    private static void FillUncoveredPixels(Image<Rgba32> canvas, bool[] filledPixels)
    {
        canvas.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                int pixelBaseIndex = y * canvas.Width;
                for (int x = 0; x < row.Length; x++)
                {
                    if (filledPixels[pixelBaseIndex + x])
                    {
                        row[x].A = byte.MaxValue;
                        continue;
                    }

                    row[x] = DefaultDemGroundFillColor;
                }
            }
        });
    }

    private static GeneratedTerrainTexture CreateGeneratedTexture(
        Image<Rgba32> image,
        int maxTextureSize,
        string identity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTextureSize);
        using Image<Rgba32> opaqueImage = CreateOpaqueGroundImage(image);

        if (TryCreatePowerOfTwoCanvasTexture(opaqueImage, maxTextureSize, identity, out GeneratedTerrainTexture? generatedTexture))
        {
            return generatedTexture!;
        }

        int fallbackMaxTextureSize = RoundDownToPowerOfTwo(maxTextureSize);
        using Image<Rgba32> resizedImage = ResizeToMaxTextureSize(opaqueImage, fallbackMaxTextureSize);
        if (TryCreatePowerOfTwoCanvasTexture(resizedImage, fallbackMaxTextureSize, identity, out generatedTexture))
        {
            return generatedTexture!;
        }

        throw new InvalidOperationException(
            $"Terrain texture fallback failed to fit into a power-of-two canvas within maxTextureSize={maxTextureSize}.");
    }

    private static bool TryCreatePowerOfTwoCanvasTexture(
        Image<Rgba32> image,
        int maxTextureSize,
        string identity,
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
        canvasImage.Mutate(context => context.DrawImage(
            image,
            new Point(0, canvasHeight - image.Height),
            1.0f));
        generatedTexture = new GeneratedTerrainTexture(
            CreateRawTextureImport(canvasImage, identity),
            new ResoniteFloat2((double)image.Width / canvasWidth, (double)image.Height / canvasHeight),
            new ResoniteFloat2(0.0, 0.0));
        return true;
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

    private static string CreateOverlayIdentity(
        TerrainTextureOverlay terrainTextureOverlay,
        IReadOnlyList<TerrainTextureSource> usedSources)
    {
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"terrain-overlay/{terrainTextureOverlay.PackageName}/{string.Join("|and|", usedSources.Select(static source => source.IdentityKey))}/"
            + $"{terrainTextureOverlay.GeographicBounds.MinLatitude:0.######},"
            + $"{terrainTextureOverlay.GeographicBounds.MaxLatitude:0.######},"
            + $"{terrainTextureOverlay.GeographicBounds.MinLongitude:0.######},"
            + $"{terrainTextureOverlay.GeographicBounds.MaxLongitude:0.######}");
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

    private static ResoniteRawTextureImport CreateRawTextureImport(Image<Rgba32> image, string identity)
    {
        byte[] rawBytes = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(rawBytes);
        return new ResoniteRawTextureImport(
            image.Width,
            image.Height,
            "sRGB",
            rawBytes,
            identity);
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

    private static bool IsGsiFallbackSource(TerrainTextureSource source)
    {
        return source is TerrainTextureTileSource tileSource
            && tileSource.ZoomLevel == 18
            && tileSource.UrlTemplate.Contains("cyberjapandata.gsi.go.jp/xyz/seamlessphoto/", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record CachedTerrainTexture(
        GeneratedTerrainTexture GeneratedTexture,
        TerrainTextureSource UsedSource,
        IReadOnlyList<TerrainTextureSource> AdditionalUsedSources,
        bool UsesGsiFallbackLicense);

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

internal sealed record TerrainTextureGenerationResult(
    GeneratedTerrainTexture GeneratedTexture,
    TerrainTextureSource UsedSource,
    IReadOnlyList<TerrainTextureSource>? AdditionalUsedSources = null,
    bool UsesGsiFallbackLicense = false)
{
    public ResoniteRawTextureImport TextureImport => GeneratedTexture.TextureImport;

    public IReadOnlyList<TerrainTextureSource> UsedSources =>
        AdditionalUsedSources is { Count: > 0 }
            ? [UsedSource, .. AdditionalUsedSources]
            : [UsedSource];
}

internal sealed class RenderedTerrainTextureSource(
    Image<Rgba32> image,
    TerrainTextureSource source,
    GeographicRectangle coverageBounds,
    int? preferredCanvasWidth,
    int? preferredCanvasHeight) : IDisposable
{
    public Image<Rgba32> Image { get; } = image;
    public TerrainTextureSource Source { get; } = source;
    public GeographicRectangle CoverageBounds { get; } = coverageBounds;
    public int? PreferredCanvasWidth { get; } = preferredCanvasWidth;
    public int? PreferredCanvasHeight { get; } = preferredCanvasHeight;

    public void Dispose()
    {
        Image.Dispose();
    }
}

internal sealed class TerrainTextureCanvasLayout(
    GeographicRectangle overlayBounds,
    int width,
    int height)
{
    public GeographicRectangle OverlayBounds { get; } = overlayBounds;
    public int Width { get; } = width;
    public int Height { get; } = height;

    public static TerrainTextureCanvasLayout Create(
        GeographicRectangle overlayBounds,
        RenderedTerrainTextureSource seedSource)
    {
        if (seedSource.PreferredCanvasWidth is int preferredWidth
            && seedSource.PreferredCanvasHeight is int preferredHeight)
        {
            return new TerrainTextureCanvasLayout(
                overlayBounds,
                preferredWidth,
                preferredHeight);
        }

        (double overlayWest, double overlayEast, double overlayNorth, double overlaySouth) = ToMercatorBounds(overlayBounds);
        (double coverageWest, double coverageEast, double coverageNorth, double coverageSouth) = ToMercatorBounds(seedSource.CoverageBounds);
        double overlayWidth = overlayEast - overlayWest;
        double overlayHeight = overlaySouth - overlayNorth;
        double coverageWidth = coverageEast - coverageWest;
        double coverageHeight = coverageSouth - coverageNorth;
        if (overlayWidth <= 1e-12
            || overlayHeight <= 1e-12
            || coverageWidth <= 1e-12
            || coverageHeight <= 1e-12)
        {
            throw new InvalidOperationException("Terrain texture overlay has degenerate geographic bounds.");
        }

        double pixelsPerMercatorX = seedSource.Image.Width / coverageWidth;
        double pixelsPerMercatorY = seedSource.Image.Height / coverageHeight;
        return new TerrainTextureCanvasLayout(
            overlayBounds,
            Math.Max(1, (int)Math.Round(overlayWidth * pixelsPerMercatorX)),
            Math.Max(1, (int)Math.Round(overlayHeight * pixelsPerMercatorY)));
    }

    public Rectangle GetTargetRectangle(GeographicRectangle coverageBounds)
    {
        (double overlayWest, double overlayEast, double overlayNorth, double overlaySouth) = ToMercatorBounds(OverlayBounds);
        (double coverageWest, double coverageEast, double coverageNorth, double coverageSouth) = ToMercatorBounds(coverageBounds);
        double overlayWidth = overlayEast - overlayWest;
        double overlayHeight = overlaySouth - overlayNorth;

        int left = Math.Clamp((int)Math.Floor(((coverageWest - overlayWest) / overlayWidth) * Width), 0, Width - 1);
        int top = Math.Clamp((int)Math.Floor(((coverageNorth - overlayNorth) / overlayHeight) * Height), 0, Height - 1);
        int right = Math.Clamp((int)Math.Ceiling(((coverageEast - overlayWest) / overlayWidth) * Width), left + 1, Width);
        int bottom = Math.Clamp((int)Math.Ceiling(((coverageSouth - overlayNorth) / overlayHeight) * Height), top + 1, Height);
        return new Rectangle(left, top, right - left, bottom - top);
    }

    private static (double West, double East, double North, double South) ToMercatorBounds(GeographicRectangle bounds)
    {
        return (
            WebMercatorTileMath.LongitudeToNormalizedX(bounds.MinLongitude),
            WebMercatorTileMath.LongitudeToNormalizedX(bounds.MaxLongitude),
            WebMercatorTileMath.LatitudeToNormalizedY(bounds.MaxLatitude),
            WebMercatorTileMath.LatitudeToNormalizedY(bounds.MinLatitude));
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
            "Plateau.ResoniteLink",
            "terrain-tile-cache");
    }
}
