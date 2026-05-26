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

    public async Task<TerrainTextureSourceImage?> TryCreateAsync(
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
