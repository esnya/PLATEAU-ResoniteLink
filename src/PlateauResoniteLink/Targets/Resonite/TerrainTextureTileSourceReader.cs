using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class TerrainTextureTileSourceReader(
    HttpClient httpClient,
    PersistentTerrainTileCache? persistentTileCache)
{
    private const int MaxTileDownloadAttempts = 4;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The returned read result transfers TerrainTextureSourceImage ownership to TerrainTextureAssetGenerator, which disposes it after composition.")]
    public async Task<TerrainTextureSourceReadResult> TryCreateAsync(
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
                TerrainTextureTileReadResult tileResult = await TryDownloadTileAsync(
                    tileSource,
                    tileX,
                    tileY,
                    cancellationToken);
                if (tileResult.Kind == TerrainTextureTileReadResultKind.SourceFailure)
                {
                    return TerrainTextureSourceReadResult.SourceFailure(tileResult.FailureMessage!);
                }

                Image<Rgba32>? tileImage = tileResult.Image;
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
            return TerrainTextureSourceReadResult.CoverageMiss;
        }

        return TerrainTextureSourceReadResult.Rendered(
            new TerrainTextureSourceImage(
                stitchedImage.Clone(context => context.Crop(new Rectangle(
                    tileCrop.CropLeft,
                    tileCrop.CropTop,
                    tileCrop.CropWidth,
                    tileCrop.CropHeight))),
                tileCrop.OccupiedUvRect));
    }

    private static ExpandedTileCrop CreateExpandedTileCrop(
        TerrainTextureLayoutPlan layoutPlan,
        int zoomLevel,
        int maxTextureSize)
    {
        int canvasWidth = TexturePowerOfTwo.RoundUp(layoutPlan.CropWidth);
        int canvasHeight = TexturePowerOfTwo.RoundUp(layoutPlan.CropHeight);
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
        int maxTileIndex = checked((int)((1U << zoomLevel) - 1U));
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

    private async Task<TerrainTextureTileReadResult> TryDownloadTileAsync(
        TerrainTextureTileSource tileSource,
        int tileX,
        int tileY,
        CancellationToken cancellationToken)
    {
        string tileUrl = WebMercatorTileMath.FormatTileUrl(tileSource.UrlTemplate, tileSource.ZoomLevel, tileX, tileY);
        if (persistentTileCache is not null)
        {
            await using Stream? cachedStream = await persistentTileCache.TryOpenTileReadAsync(
                tileSource.UrlTemplate,
                tileSource.ZoomLevel,
                tileX,
                tileY,
                cancellationToken);
            if (cachedStream is not null)
            {
                try
                {
                    return TerrainTextureTileReadResult.Rendered(
                        await LoadTileImageAsync(cachedStream, cancellationToken));
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

                    return IsTransientStatusCode((int)response.StatusCode)
                        ? TerrainTextureTileReadResult.SourceFailure(
                            $"DEM terrain tile source '{tileSource.UrlTemplate}' returned transient HTTP {(int)response.StatusCode} for z={tileSource.ZoomLevel}, x={tileX}, y={tileY} after {MaxTileDownloadAttempts} attempts.")
                        : TerrainTextureTileReadResult.CoverageMiss;
                }

                await using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                try
                {
                    if (persistentTileCache is not null)
                    {
                        using MemoryStream retainedContent = new();
                        await responseStream.CopyToAsync(retainedContent, cancellationToken);
                        retainedContent.Position = 0;
                        await TryWritePersistentTileAsync(
                            tileSource.UrlTemplate,
                            tileSource.ZoomLevel,
                            tileX,
                            tileY,
                            retainedContent,
                            cancellationToken);
                        retainedContent.Position = 0;
                        return TerrainTextureTileReadResult.Rendered(
                            await LoadTileImageAsync(retainedContent, cancellationToken));
                    }

                    return TerrainTextureTileReadResult.Rendered(
                        await LoadTileImageAsync(responseStream, cancellationToken));
                }
                catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException)
                {
                    return TerrainTextureTileReadResult.SourceFailure(
                        $"DEM terrain tile source '{tileSource.UrlTemplate}' returned invalid image content for z={tileSource.ZoomLevel}, x={tileX}, y={tileY}: {exception.Message}");
                }
            }
            catch (HttpRequestException) when (attempt < MaxTileDownloadAttempts)
            {
                await Task.Delay(GetRetryDelay(attempt), cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                return TerrainTextureTileReadResult.SourceFailure(
                    $"DEM terrain tile source '{tileSource.UrlTemplate}' failed for z={tileSource.ZoomLevel}, x={tileX}, y={tileY}: {exception.Message}");
            }
        }

        return TerrainTextureTileReadResult.SourceFailure(
            $"DEM terrain tile source '{tileSource.UrlTemplate}' failed for z={tileSource.ZoomLevel}, x={tileX}, y={tileY} after {MaxTileDownloadAttempts} attempts.");
    }

    private async Task TryWritePersistentTileAsync(
        string urlTemplate,
        int zoomLevel,
        int tileX,
        int tileY,
        Stream encodedContent,
        CancellationToken cancellationToken)
    {
        if (persistentTileCache is null)
        {
            return;
        }

        try
        {
            await persistentTileCache.WriteTileAsync(
                urlTemplate,
                zoomLevel,
                tileX,
                tileY,
                encodedContent,
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
        Stream encodedContent,
        CancellationToken cancellationToken)
    {
        return await Image.LoadAsync<Rgba32>(encodedContent, cancellationToken);
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
}

internal sealed record TerrainTextureSourceReadResult(
    TerrainTextureSourceReadResultKind Kind,
    TerrainTextureSourceImage? Image,
    string? FailureMessage)
{
    public static TerrainTextureSourceReadResult CoverageMiss { get; } = new(
        TerrainTextureSourceReadResultKind.CoverageMiss,
        Image: null,
        FailureMessage: null);

    public static TerrainTextureSourceReadResult Rendered(TerrainTextureSourceImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return new TerrainTextureSourceReadResult(
            TerrainTextureSourceReadResultKind.Rendered,
            image,
            FailureMessage: null);
    }

    public static TerrainTextureSourceReadResult SourceFailure(string failureMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);
        return new TerrainTextureSourceReadResult(
            TerrainTextureSourceReadResultKind.SourceFailure,
            Image: null,
            failureMessage);
    }
}

internal enum TerrainTextureSourceReadResultKind
{
    Rendered,
    CoverageMiss,
    SourceFailure,
}

internal sealed record TerrainTextureTileReadResult(
    TerrainTextureTileReadResultKind Kind,
    Image<Rgba32>? Image,
    string? FailureMessage)
{
    public static TerrainTextureTileReadResult CoverageMiss { get; } = new(
        TerrainTextureTileReadResultKind.CoverageMiss,
        Image: null,
        FailureMessage: null);

    public static TerrainTextureTileReadResult Rendered(Image<Rgba32> image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return new TerrainTextureTileReadResult(
            TerrainTextureTileReadResultKind.Rendered,
            image,
            FailureMessage: null);
    }

    public static TerrainTextureTileReadResult SourceFailure(string failureMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);
        return new TerrainTextureTileReadResult(
            TerrainTextureTileReadResultKind.SourceFailure,
            Image: null,
            failureMessage);
    }
}

internal enum TerrainTextureTileReadResultKind
{
    Rendered,
    CoverageMiss,
    SourceFailure,
}
