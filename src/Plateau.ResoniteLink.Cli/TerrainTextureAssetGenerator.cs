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

    void ResetUsageTracking();

    ResoniteLicenseComponentMetadata ResolveDatasetLicense(ResoniteLicenseComponentMetadata baseLicense);
}

internal sealed class TerrainTextureAssetGenerator(HttpClient? httpClient = null) : ITerrainTextureAssetGenerator
{
    private readonly HttpClient httpClient = httpClient ?? new HttpClient();
    private readonly AsyncCompletedResultCache<TerrainTextureOverlay, ResoniteRawTextureImport> cachedTextures = new();
    private int usedTerrainTileCount;
    private int fallbackTileUseCount;

    public async Task<ResoniteRawTextureImport> EnsureTextureAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureOverlay);

        return await cachedTextures.GetOrCreateAsync(
            terrainTextureOverlay,
            ct => CreateTextureAsync(terrainTextureOverlay, ct),
            cancellationToken);
    }

    public void ResetUsageTracking()
    {
        Interlocked.Exchange(ref usedTerrainTileCount, 0);
        Interlocked.Exchange(ref fallbackTileUseCount, 0);
    }

    public ResoniteLicenseComponentMetadata ResolveDatasetLicense(ResoniteLicenseComponentMetadata baseLicense)
    {
        ArgumentNullException.ThrowIfNull(baseLicense);

        int usedTerrainTiles = Interlocked.Exchange(ref usedTerrainTileCount, 0);
        int fallbackTerrainTiles = Interlocked.Exchange(ref fallbackTileUseCount, 0);

        if (usedTerrainTiles == 0)
        {
            return baseLicense;
        }

        if (fallbackTerrainTiles == 0)
        {
            return baseLicense with
            {
                CreditText = $"{baseLicense.CreditText} DEM terrain imagery used Project PLATEAU Ortho xyz tiles.",
                LicenseName = "PLATEAU Open Data Terms + Project PLATEAU Site Policy",
                LicenseUrl = "https://www.mlit.go.jp/plateau/site-policy/",
            };
        }

        return baseLicense with
        {
            CreditText = $"{baseLicense.CreditText} DEM terrain imagery used Project PLATEAU Ortho xyz tiles with fallback to GSI seamless photo tiles where PLATEAU-Ortho coverage was unavailable.",
            LicenseName = "PLATEAU Open Data Terms + GSI Maps Terms",
            LicenseUrl = "https://maps.gsi.go.jp/help/termsofuse.html",
        };
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
        return CreateRawTextureImport(outputImage, CreateOverlayIdentity(terrainTextureOverlay));
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
        Image<Rgba32>? primaryImage = await TryDownloadTileAsync(
            terrainTextureOverlay.UrlTemplate,
            tileX,
            tileY,
            terrainTextureOverlay.ZoomLevel,
            cancellationToken);
        if (primaryImage is not null)
        {
            _ = Interlocked.Increment(ref usedTerrainTileCount);
            return primaryImage;
        }

        return await DownloadFallbackTileAsync(terrainTextureOverlay, tileX, tileY, cancellationToken);
    }

    private async Task<Image<Rgba32>> DownloadFallbackTileAsync(
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

        _ = Interlocked.Increment(ref usedTerrainTileCount);
        _ = Interlocked.Increment(ref fallbackTileUseCount);
        return image;
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

            await using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await Image.LoadAsync<Rgba32>(responseStream, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
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
}
