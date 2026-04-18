using System.Security.Cryptography;
using System.Text;

using Plateau.ResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Plateau.ResoniteLink.Targets.Resonite;

internal interface ITerrainTextureAssetGenerator
{
    Task<ResoniteRawTextureImport> EnsureTextureAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken);
}

internal sealed class TerrainTextureAssetGenerator(
    HttpClient? httpClient = null,
    string? persistentCacheRoot = null,
    bool disablePersistentCache = false) : ITerrainTextureAssetGenerator
{
    private readonly HttpClient httpClient = httpClient ?? new HttpClient();
    private readonly AsyncCompletedResultCache<TerrainTextureOverlay, CachedTerrainTexture> cachedTextures = new();
    private readonly PersistentTerrainTileCache? persistentTileCache = disablePersistentCache
        ? null
        : new PersistentTerrainTileCache(persistentCacheRoot);

    public async Task<ResoniteRawTextureImport> EnsureTextureAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureOverlay);

        CachedTerrainTexture cachedTexture = await cachedTextures.GetOrCreateAsync(
            terrainTextureOverlay,
            ct => CreateTextureAsync(terrainTextureOverlay, ct),
            cancellationToken);
        return cachedTexture.TextureImport;
    }

    private async Task<CachedTerrainTexture> CreateTextureAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken)
    {
        TerrainTextureLayoutPlan layoutPlan = TerrainTextureLayoutPlanner.Create(terrainTextureOverlay);
        int usedTerrainTiles = 0;
        int fallbackTerrainTiles = 0;

        using Image<Rgba32> stitchedImage = new(
            layoutPlan.StitchedWidth,
            layoutPlan.StitchedHeight);

        for (int tileY = layoutPlan.MinTileY; tileY <= layoutPlan.MaxTileY; tileY++)
        {
            for (int tileX = layoutPlan.MinTileX; tileX <= layoutPlan.MaxTileX; tileX++)
            {
                DownloadedTerrainTile tile = await DownloadTileAsync(terrainTextureOverlay, tileX, tileY, cancellationToken);
                usedTerrainTiles += tile.UsedTerrainTileCount;
                fallbackTerrainTiles += tile.FallbackTileUseCount;
                using Image<Rgba32> tileImage = tile.Image;
                stitchedImage.Mutate(context => context.DrawImage(
                    tileImage,
                    new Point(
                        (tileX - layoutPlan.MinTileX) * WebMercatorTileMath.TileSizePixels,
                        (tileY - layoutPlan.MinTileY) * WebMercatorTileMath.TileSizePixels),
                    1.0f));
            }
        }

        using Image<Rgba32> croppedImage = stitchedImage.Clone(context => context.Crop(new Rectangle(
            layoutPlan.CropLeft,
            layoutPlan.CropTop,
            layoutPlan.CropWidth,
            layoutPlan.CropHeight)));

        using Image<Rgba32> outputImage = ResizeToMaxTextureSize(croppedImage, terrainTextureOverlay.MaxTextureSize);
        return new CachedTerrainTexture(
            CreateRawTextureImport(outputImage, CreateOverlayIdentity(terrainTextureOverlay)),
            usedTerrainTiles,
            fallbackTerrainTiles);
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

    private async Task<DownloadedTerrainTile> DownloadTileAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        int tileX,
        int tileY,
        CancellationToken cancellationToken)
    {
        Image<Rgba32>? primaryImage = await TryDownloadTileAsync(
            terrainTextureOverlay.UrlTemplate,
            tileX,
            tileY,
            terrainTextureOverlay.ZoomLevel,
            cancellationToken);
        if (primaryImage is not null)
        {
            return new DownloadedTerrainTile(primaryImage, UsedTerrainTileCount: 1, FallbackTileUseCount: 0);
        }

        return await DownloadFallbackTileAsync(terrainTextureOverlay, tileX, tileY, cancellationToken);
    }

    private async Task<DownloadedTerrainTile> DownloadFallbackTileAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        int tileX,
        int tileY,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(terrainTextureOverlay.FallbackUrlTemplate))
        {
            throw new HttpRequestException(
                $"Terrain texture tile download failed for '{CreateOverlayIdentity(terrainTextureOverlay)}' at {terrainTextureOverlay.ZoomLevel}/{tileX}/{tileY}.");
        }

        Image<Rgba32>? image = await TryDownloadTileAsync(
            terrainTextureOverlay.FallbackUrlTemplate,
            tileX,
            tileY,
            terrainTextureOverlay.ZoomLevel,
            cancellationToken);
        if (image is null)
        {
            throw new HttpRequestException(
                $"Terrain texture tile download failed for both primary and fallback sources at {terrainTextureOverlay.ZoomLevel}/{tileX}/{tileY}.");
        }

        return new DownloadedTerrainTile(image, UsedTerrainTileCount: 1, FallbackTileUseCount: 1);
    }

    private static string CreateOverlayIdentity(TerrainTextureOverlay terrainTextureOverlay)
    {
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"terrain-overlay/{terrainTextureOverlay.PackageName}/{terrainTextureOverlay.ZoomLevel}/"
            + $"{terrainTextureOverlay.GeographicBounds.MinLatitude:0.######},"
            + $"{terrainTextureOverlay.GeographicBounds.MaxLatitude:0.######},"
            + $"{terrainTextureOverlay.GeographicBounds.MinLongitude:0.######},"
            + $"{terrainTextureOverlay.GeographicBounds.MaxLongitude:0.######}");
    }

    private async Task<Image<Rgba32>?> TryDownloadTileAsync(
        string urlTemplate,
        int tileX,
        int tileY,
        int zoomLevel,
        CancellationToken cancellationToken)
    {
        string tileUrl = WebMercatorTileMath.FormatTileUrl(urlTemplate, zoomLevel, tileX, tileY);
        if (persistentTileCache is not null)
        {
            byte[]? cachedBytes = await persistentTileCache.TryReadTileBytesAsync(
                urlTemplate,
                zoomLevel,
                tileX,
                tileY,
                cancellationToken);
            if (cachedBytes is not null)
            {
                try
                {
                    return await LoadTileImageAsync(cachedBytes, cancellationToken);
                }
                catch (SixLabors.ImageSharp.UnknownImageFormatException)
                {
                    // Corrupt persistent cache entries should behave like a cache miss.
                }
                catch (SixLabors.ImageSharp.InvalidImageContentException)
                {
                    // Corrupt persistent cache entries should behave like a cache miss.
                }
            }
        }

        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(
                tileUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            byte[] encodedBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (persistentTileCache is not null)
            {
                await TryWritePersistentTileBytesAsync(
                    urlTemplate,
                    zoomLevel,
                    tileX,
                    tileY,
                    encodedBytes,
                    cancellationToken);
            }

            return await LoadTileImageAsync(encodedBytes, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
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
            // Persistent tile caching is an optimization. A local cache write failure
            // must not discard a tile that was already downloaded successfully.
        }
        catch (UnauthorizedAccessException)
        {
            // Persistent tile caching is an optimization. A local cache write failure
            // must not discard a tile that was already downloaded successfully.
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

    private sealed record CachedTerrainTexture(
        ResoniteRawTextureImport TextureImport,
        int UsedTerrainTileCount,
        int FallbackTileUseCount);

    private sealed record DownloadedTerrainTile(
        Image<Rgba32> Image,
        int UsedTerrainTileCount,
        int FallbackTileUseCount);
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
