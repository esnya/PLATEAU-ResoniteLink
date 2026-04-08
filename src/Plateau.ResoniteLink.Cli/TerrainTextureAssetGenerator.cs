using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

using Plateau.ResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Plateau.ResoniteLink.Cli;

internal interface ITerrainTextureAssetGenerator
{
    Task<ResoniteRawTextureImport> EnsureTextureAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken);
}

internal sealed class TerrainTextureAssetGenerator(HttpClient? httpClient = null) : ITerrainTextureAssetGenerator
{
    private readonly HttpClient httpClient = httpClient ?? new HttpClient();
    private readonly ConcurrentDictionary<string, Lazy<Task<ResoniteRawTextureImport>>> cachedTextures = new(StringComparer.Ordinal);

    public async Task<ResoniteRawTextureImport> EnsureTextureAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureOverlay);
        string cacheKey = CreateCacheKey(terrainTextureOverlay);

        Lazy<Task<ResoniteRawTextureImport>> cachedTexture = cachedTextures.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<ResoniteRawTextureImport>>(
                () => CreateTextureAsync(terrainTextureOverlay, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await cachedTexture.Value;
        }
        catch
        {
            cachedTextures.TryRemove(cacheKey, out _);
            throw;
        }
    }

    private async Task<ResoniteRawTextureImport> CreateTextureAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken)
    {
        TerrainTextureLayoutPlan layoutPlan = TerrainTextureLayoutPlanner.Create(terrainTextureOverlay);

        using Image<Rgba32> stitchedImage = new(
            layoutPlan.StitchedWidth,
            layoutPlan.StitchedHeight);

        for (int tileY = layoutPlan.MinTileY; tileY <= layoutPlan.MaxTileY; tileY++)
        {
            for (int tileX = layoutPlan.MinTileX; tileX <= layoutPlan.MaxTileX; tileX++)
            {
                using Image<Rgba32> tileImage = await DownloadTileAsync(terrainTextureOverlay, tileX, tileY, cancellationToken);
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
        return CreateRawTextureImport(outputImage);
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

    private async Task<Image<Rgba32>> DownloadTileAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        int tileX,
        int tileY,
        CancellationToken cancellationToken)
    {
        string tileUrl = WebMercatorTileMath.FormatTileUrl(
            terrainTextureOverlay.UrlTemplate,
            terrainTextureOverlay.ZoomLevel,
            tileX,
            tileY);
        using HttpResponseMessage response = await httpClient.GetAsync(
            tileUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await Image.LoadAsync<Rgba32>(responseStream, cancellationToken);
    }

    private static ResoniteRawTextureImport CreateRawTextureImport(Image<Rgba32> image)
    {
        byte[] rawBytes = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(rawBytes);
        return new ResoniteRawTextureImport(image.Width, image.Height, "sRGB", rawBytes);
    }

    private static string CreateCacheKey(TerrainTextureOverlay terrainTextureOverlay)
    {
        string fingerprint = string.Join(
            "|",
            terrainTextureOverlay.TexturePath,
            terrainTextureOverlay.PackageName,
            terrainTextureOverlay.UrlTemplate,
            terrainTextureOverlay.ZoomLevel,
            terrainTextureOverlay.GeographicBounds.MinLatitude,
            terrainTextureOverlay.GeographicBounds.MaxLatitude,
            terrainTextureOverlay.GeographicBounds.MinLongitude,
            terrainTextureOverlay.GeographicBounds.MaxLongitude,
            terrainTextureOverlay.MaxTextureSize);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint));
        return Convert.ToHexString(hash);
    }
}
