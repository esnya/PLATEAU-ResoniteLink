using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class TerrainTextureAssetGeneratorTests
{
    [Fact]
    public async Task EnsureTextureAsyncStitchesTilesAndCachesOutput()
    {
        using FakeMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);

        TerrainTextureOverlay overlay = CreateFullCoverageOverlay("https://tiles.example/{z}/{x}/{y}.png");
        TerrainTextureLayoutPlan layout = TerrainTextureLayoutPlanner.Create(overlay.GeographicBounds, overlay.ZoomLevel);

        GeneratedTerrainTexture firstTexture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);
        GeneratedTerrainTexture secondTexture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        Assert.Same(firstTexture, secondTexture);
        Assert.Contains("terrain-overlay/dem/tile|1|https://tiles.example/{z}/{x}/{y}.png/", firstTexture.TextureImport.Identity, StringComparison.Ordinal);
        Assert.Equal(
            new ResoniteFloat2(
                (double)layout.CropWidth / RoundUpToPowerOfTwo(layout.CropWidth),
                (double)layout.CropHeight / RoundUpToPowerOfTwo(layout.CropHeight)),
            firstTexture.OccupiedUvRect.Scale);

        using Image<Rgba32> image = LoadImage(firstTexture.TextureImport);
        Assert.Equal(RoundUpToPowerOfTwo(layout.CropWidth), image.Width);
        Assert.Equal(RoundUpToPowerOfTwo(layout.CropHeight), image.Height);
        int occupiedTop = image.Height - layout.CropHeight;
        AssertColor(image[layout.CropWidth / 4, occupiedTop + (layout.CropHeight / 2)], 255, 0, 0);
        AssertColor(image[(layout.CropWidth * 3) / 4, occupiedTop + (layout.CropHeight / 2)], 0, 255, 0);
    }

    [Fact]
    public async Task EnsureTextureAsyncPacksIntoPowerOfTwoCanvasWithoutResizing()
    {
        using FakeMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);

        TerrainTextureOverlay overlay = CreateFullCoverageOverlay("https://tiles.example/{z}/{x}/{y}.png") with
        {
            MaxTextureSize = 256,
        };

        GeneratedTerrainTexture texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        using Image<Rgba32> image = LoadImage(texture.TextureImport);
        Assert.Equal(256, image.Width);
        Assert.Equal(128, image.Height);
        Assert.Equal(new ResoniteFloat2(1.0, 1.0), texture.OccupiedUvRect.Scale);
        AssertColor(image[64, 32], 255, 0, 0);
        AssertColor(image[192, 32], 0, 255, 0);
    }

    [Fact]
    public async Task EnsureTextureAsyncUsesPowerOfTwoCanvasForCropWithinBudget()
    {
        using FakeMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);
        GeographicRectangle bounds = new(
            MinLatitude: WebMercatorTileMath.PixelYToLatitude(100, 1),
            MaxLatitude: WebMercatorTileMath.PixelYToLatitude(0, 1),
            MinLongitude: WebMercatorTileMath.PixelXToLongitude(0, 1),
            MaxLongitude: WebMercatorTileMath.PixelXToLongitude(400, 1));
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
            ZoomLevel: 1,
            GeographicBounds: bounds,
            MaxTextureSize: 512);
        TerrainTextureLayoutPlan layout = TerrainTextureLayoutPlanner.Create(overlay.GeographicBounds, overlay.ZoomLevel);

        GeneratedTerrainTexture texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        using Image<Rgba32> image = LoadImage(texture.TextureImport);
        Assert.Equal(RoundUpToPowerOfTwo(layout.CropWidth), texture.TextureImport.Width);
        Assert.Equal(RoundUpToPowerOfTwo(layout.CropHeight), texture.TextureImport.Height);
        Assert.Equal(
            new ResoniteFloat2(
                (double)layout.CropWidth / RoundUpToPowerOfTwo(layout.CropWidth),
                (double)layout.CropHeight / RoundUpToPowerOfTwo(layout.CropHeight)),
            texture.OccupiedUvRect.Scale);
        Assert.Equal(
            new ResoniteFloat2(
                0.0,
                0.0),
            texture.OccupiedUvRect.Offset);
        int occupiedTop = image.Height - layout.CropHeight;
        Assert.Equal(TerrainTextureAssetGenerator.DefaultDemGroundFillColor, image[0, 0]);
        AssertColor(image[layout.CropWidth / 4, occupiedTop + (layout.CropHeight / 2)], 255, 0, 0);
        AssertColor(image[(layout.CropWidth * 3) / 4, occupiedTop + (layout.CropHeight / 2)], 0, 255, 0);
        Assert.True(occupiedTop > 0);
    }

    [Fact]
    public async Task EnsureTextureAsyncFallsBackToPowerOfTwoResizeWhenPowerOfTwoCanvasWouldExceedBudget()
    {
        using FakeMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);

        TerrainTextureOverlay overlay = CreateFullCoverageOverlay("https://tiles.example/{z}/{x}/{y}.png") with
        {
            MaxTextureSize = 300,
        };

        GeneratedTerrainTexture texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        using Image<Rgba32> image = LoadImage(texture.TextureImport);
        Assert.Equal(256, image.Width);
        Assert.Equal(128, image.Height);
        Assert.Equal(new ResoniteFloat2(1.0, 1.0), texture.OccupiedUvRect.Scale);
    }

    [Fact]
    public async Task EnsureTextureAsyncKeepsNorthUpOrientationAcrossStitchedTiles()
    {
        using FakeMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);

        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
            ZoomLevel: 1,
            GeographicBounds: new GeographicRectangle(
                MinLatitude: -WebMercatorTileMath.MaxLatitude,
                MaxLatitude: WebMercatorTileMath.MaxLatitude,
                MinLongitude: -180.0,
                MaxLongitude: 180.0),
            MaxTextureSize: 4096);
        TerrainTextureLayoutPlan layout = TerrainTextureLayoutPlanner.Create(overlay.GeographicBounds, overlay.ZoomLevel);

        GeneratedTerrainTexture texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        using Image<Rgba32> image = LoadImage(texture.TextureImport);
        Assert.Equal(RoundUpToPowerOfTwo(layout.CropWidth), image.Width);
        Assert.Equal(RoundUpToPowerOfTwo(layout.CropHeight), image.Height);
        int occupiedTop = image.Height - layout.CropHeight;
        AssertColor(image[layout.CropWidth / 4, occupiedTop + (layout.CropHeight / 4)], 255, 0, 0);
        AssertColor(image[(layout.CropWidth * 3) / 4, occupiedTop + (layout.CropHeight / 4)], 0, 255, 0);
        AssertColor(image[layout.CropWidth / 4, occupiedTop + ((layout.CropHeight * 3) / 4)], 0, 0, 255);
        AssertColor(image[(layout.CropWidth * 3) / 4, occupiedTop + ((layout.CropHeight * 3) / 4)], 255, 255, 0);
    }

    [Fact]
    public async Task EnsureTextureAsyncSharesConcurrentRequestsForTheSameOverlay()
    {
        using FakeMapTileHandler handler = new(delayPerRequest: TimeSpan.FromMilliseconds(50));
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);
        TerrainTextureOverlay overlay = CreateFullCoverageOverlay("https://tiles.example/{z}/{x}/{y}.png");

        Task<GeneratedTerrainTexture>[] requests =
        [
            generator.EnsureTextureAsync(overlay, CancellationToken.None),
            generator.EnsureTextureAsync(overlay, CancellationToken.None),
            generator.EnsureTextureAsync(overlay, CancellationToken.None),
        ];

        GeneratedTerrainTexture[] textures = await Task.WhenAll(requests);

        Assert.All(textures, texture => Assert.Same(textures[0], texture));
        Assert.Equal(4, handler.RequestCount);
    }

    [Fact]
    public async Task EnsureTextureAsyncReusesPersistentTileCacheAcrossGeneratorInstances()
    {
        using TemporaryDirectory cacheRoot = new();
        TerrainTextureOverlay overlay = CreateFullCoverageOverlay("https://tiles.example/{z}/{x}/{y}.png");

        using (FakeMapTileHandler firstHandler = new())
        using (HttpClient firstClient = new(firstHandler))
        {
            TerrainTextureAssetGenerator firstGenerator = new(firstClient, cacheRoot.Path);
            _ = await firstGenerator.EnsureTextureAsync(overlay, CancellationToken.None);
            Assert.Equal(4, firstHandler.RequestCount);
        }

        using FakeMapTileHandler secondHandler = new();
        using HttpClient secondClient = new(secondHandler);
        TerrainTextureAssetGenerator secondGenerator = new(secondClient, cacheRoot.Path);

        GeneratedTerrainTexture texture = await secondGenerator.EnsureTextureAsync(overlay, CancellationToken.None);

        Assert.Equal(0, secondHandler.RequestCount);
        Assert.Contains("terrain-overlay/dem/tile|1|https://tiles.example/{z}/{x}/{y}.png/", texture.TextureImport.Identity, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnsureTextureAsyncSkipsPersistentTileCacheWhenDisabled()
    {
        using TemporaryDirectory cacheRoot = new();
        TerrainTextureOverlay overlay = CreateFullCoverageOverlay("https://tiles.example/{z}/{x}/{y}.png");

        using (FakeMapTileHandler firstHandler = new())
        using (HttpClient firstClient = new(firstHandler))
        {
            TerrainTextureAssetGenerator firstGenerator = new(firstClient, cacheRoot.Path);
            _ = await firstGenerator.EnsureTextureAsync(overlay, CancellationToken.None);
            Assert.Equal(4, firstHandler.RequestCount);
        }

        using FakeMapTileHandler secondHandler = new();
        using HttpClient secondClient = new(secondHandler);
        TerrainTextureAssetGenerator secondGenerator = new(secondClient, cacheRoot.Path, disablePersistentCache: true);

        _ = await secondGenerator.EnsureTextureAsync(overlay, CancellationToken.None);

        Assert.Equal(4, secondHandler.RequestCount);
    }

    [Fact]
    public async Task EnsureTextureAsyncTreatsPersistentTileCacheWriteFailuresAsNonFatal()
    {
        using TemporaryDirectory workRoot = new();
        string cacheRootPath = Path.Combine(workRoot.Path, "cache-root-file");
        await File.WriteAllTextAsync(cacheRootPath, "not-a-directory");

        using FakeMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, cacheRootPath);
        TerrainTextureOverlay overlay = CreateFullCoverageOverlay("https://tiles.example/{z}/{x}/{y}.png");

        GeneratedTerrainTexture texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        Assert.Equal(512, texture.TextureImport.Width);
        Assert.Equal(4, handler.RequestCount);
    }

    [Fact]
    public async Task EnsureTextureAsyncRemovesFaultedSharedGenerationAndRetries()
    {
        using FlakyMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);
        TerrainTextureOverlay overlay = CreateFullCoverageOverlay("https://tiles.example/{z}/{x}/{y}.png");

        await Assert.ThrowsAsync<HttpRequestException>(() => generator.EnsureTextureAsync(overlay, CancellationToken.None));
        GeneratedTerrainTexture texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        Assert.Equal(512, texture.TextureImport.Width);
        Assert.Equal(8, handler.RequestCount);
    }

    [Fact]
    public async Task EnsureTextureAsyncDoesNotPersistUnsuccessfulTileResponses()
    {
        using TemporaryDirectory cacheRoot = new();
        TerrainTextureOverlay overlay = CreateFullCoverageOverlay("https://tiles.example/{z}/{x}/{y}.png");

        using (FlakyMapTileHandler firstHandler = new())
        using (HttpClient firstClient = new(firstHandler))
        {
            TerrainTextureAssetGenerator firstGenerator = new(firstClient, cacheRoot.Path);
            await Assert.ThrowsAsync<HttpRequestException>(() => firstGenerator.EnsureTextureAsync(overlay, CancellationToken.None));
        }

        using RetryableMapTileHandler secondHandler = new();
        using HttpClient secondClient = new(secondHandler);
        TerrainTextureAssetGenerator secondGenerator = new(secondClient, cacheRoot.Path);

        GeneratedTerrainTexture texture = await secondGenerator.EnsureTextureAsync(overlay, CancellationToken.None);

        Assert.Equal(512, texture.TextureImport.Width);
        Assert.Equal(1, secondHandler.RequestCount);
    }

    [Fact]
    public async Task EnsureTextureAsyncRefetchesWhenPersistentCacheEntryIsCorrupt()
    {
        using TemporaryDirectory cacheRoot = new();
        TerrainTextureOverlay overlay = CreateFullCoverageOverlay("https://tiles.example/{z}/{x}/{y}.png");
        PersistentTerrainTileCache persistentCache = new(cacheRoot.Path);
        await persistentCache.WriteTileBytesAsync(
            overlay.UrlTemplate,
            overlay.ZoomLevel,
            0,
            0,
            [1, 2, 3, 4],
            CancellationToken.None);

        using FakeMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, cacheRoot.Path);

        GeneratedTerrainTexture texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        Assert.Equal(512, texture.TextureImport.Width);
        Assert.Equal(4, handler.RequestCount);
    }

    [Fact]
    public async Task EnsureTextureAsyncUsesFallbackTilesWhenPrimaryCoverageIsUnavailable()
    {
        using PrimaryFallbackMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);
        TerrainTextureOverlay overlay = CreateFullCoverageOverlay(
            "https://primary.example/{z}/{x}/{y}.png",
            "https://fallback.example/{z}/{x}/{y}.png");
        TerrainTextureLayoutPlan layout = TerrainTextureLayoutPlanner.Create(overlay.GeographicBounds, overlay.ZoomLevel);

        GeneratedTerrainTexture texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        using Image<Rgba32> image = LoadImage(texture.TextureImport);
        int occupiedTop = image.Height - layout.CropHeight;
        AssertColor(image[layout.CropWidth / 4, occupiedTop + (layout.CropHeight / 2)], 255, 0, 0);
        AssertColor(image[(layout.CropWidth * 3) / 4, occupiedTop + (layout.CropHeight / 2)], 0, 255, 0);
    }

    [Fact]
    public async Task EnsureTextureAsyncUsesSecondaryPrimaryTilesBeforeGsiFallback()
    {
        using SecondaryPrimaryFallbackMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            GeographicBounds: new GeographicRectangle(
                MinLatitude: 0.0,
                MaxLatitude: WebMercatorTileMath.MaxLatitude,
                MinLongitude: -180.0,
                MaxLongitude: 180.0),
            MaxTextureSize: 4096,
            Sources:
            [
                new TerrainTextureTileSource("https://primary.example/{z}/{x}/{y}.png", 2),
                new TerrainTextureTileSource("https://primary.example/{z}/{x}/{y}.png", 1),
                new TerrainTextureTileSource("https://fallback.example/{z}/{x}/{y}.png", 1),
            ]);

        GeneratedTerrainTexture texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        Assert.Contains("tile|1|https://primary.example/{z}/{x}/{y}.png", texture.TextureImport.Identity, StringComparison.Ordinal);
        Assert.DoesNotContain("fallback.example", texture.TextureImport.Identity, StringComparison.Ordinal);
        Assert.Contains(("primary.example", 2), handler.HostZoomRequests);
        Assert.Contains(("primary.example", 1), handler.HostZoomRequests);
        Assert.DoesNotContain(("fallback.example", 1), handler.HostZoomRequests);
    }

    [Fact]
    public async Task EnsureTextureAsyncUsesFallbackSourceZoomLevelIndependently()
    {
        using ZoomAwareFallbackMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);
        const int primaryZoomLevel = 2;
        GeographicRectangle bounds = new(
            MinLatitude: WebMercatorTileMath.PixelYToLatitude(WebMercatorTileMath.TileSizePixels, primaryZoomLevel),
            MaxLatitude: WebMercatorTileMath.PixelYToLatitude(0, primaryZoomLevel),
            MinLongitude: WebMercatorTileMath.PixelXToLongitude(0, primaryZoomLevel),
            MaxLongitude: WebMercatorTileMath.PixelXToLongitude(WebMercatorTileMath.TileSizePixels, primaryZoomLevel));
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            GeographicBounds: bounds,
            MaxTextureSize: 4096,
            PrimarySource: new TerrainTextureTileSource("https://primary.example/{z}/{x}/{y}.png", primaryZoomLevel),
            FallbackSource: new TerrainTextureTileSource("https://fallback.example/{z}/{x}/{y}.png", 1));

        _ = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        string[] requestedSources = handler.Requests
            .Select(static request => $"{request.Host}|{request.ZoomLevel}")
            .ToArray();
        Assert.Contains($"primary.example|{primaryZoomLevel}", requestedSources);
        Assert.Contains("fallback.example|1", requestedSources);
    }

    [Fact]
    public async Task EnsureTextureAsyncPartiallyFallsBackFromPrimaryToSecondarySource()
    {
        using PartialSourceFallbackMapTileHandler handler = new((1, 1));
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            GeographicBounds: new GeographicRectangle(
                MinLatitude: WebMercatorTileMath.PixelYToLatitude(2 * WebMercatorTileMath.TileSizePixels, 1),
                MaxLatitude: WebMercatorTileMath.PixelYToLatitude(0, 1),
                MinLongitude: WebMercatorTileMath.PixelXToLongitude(0, 1),
                MaxLongitude: WebMercatorTileMath.PixelXToLongitude(2 * WebMercatorTileMath.TileSizePixels, 1)),
            MaxTextureSize: 4096,
            PrimarySource: new TerrainTextureTileSource("https://primary.example/{z}/{x}/{y}.png", 1),
            FallbackSource: new TerrainTextureTileSource("https://fallback.example/{z}/{x}/{y}.png", 1));

        GeneratedTerrainTexture texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        Assert.Contains("tile|1|https://primary.example/{z}/{x}/{y}.png", texture.TextureImport.Identity, StringComparison.Ordinal);
        Assert.Contains("primary.example", handler.RequestedHosts);
        Assert.Contains("fallback.example", handler.RequestedHosts);

        using Image<Rgba32> image = LoadImage(texture.TextureImport);
        Assert.True(ContainsColor(image, 255, 0, 0));
        Assert.True(ContainsColor(image, 255, 0, 255));
    }

    [Fact]
    public async Task EnsureTextureAsyncPreservesPartialGeoReferencedRasterPlacementBeforeTileFallback()
    {
        using TemporaryDirectory workDirectory = new();
        string rasterPath = Path.Combine(workDirectory.Path, "partial-terrain.png");
        using (Image<Rgba32> rasterImage = new(2, 2, new Rgba32(255, 0, 0, 255)))
        {
            await rasterImage.SaveAsPngAsync(rasterPath);
        }

        GeographicRectangle requestedBounds = new(35.0, 35.01, 139.0, 139.02);
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            GeographicBounds: requestedBounds,
            MaxTextureSize: 16,
            Sources:
            [
                new TerrainTextureGeoReferencedRasterSource(
                    rasterPath,
                    new GeoReferencedRasterMetadata(
                        new GeographicRectangle(35.0, 35.01, 139.0, 139.01),
                        "EPSG:4326",
                        1.0,
                        1.0)),
                new TerrainTextureTileSource("https://tiles.example/{z}/{x}/{y}.png", 1),
            ]);

        using FakeMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);

        GeneratedTerrainTexture texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        using Image<Rgba32> outputImage = LoadImage(texture.TextureImport);
        Assert.Equal(new Rgba32(255, 0, 0, 255), outputImage[0, 0]);
        Assert.Equal(new Rgba32(255, 0, 0, 255), outputImage[1, 0]);
        Assert.NotEqual(new Rgba32(255, 0, 0, 255), outputImage[3, 0]);
    }
    [Fact]
    public async Task EnsureTextureAsyncRetriesTransientTileFailureWithinSingleGeneration()
    {
        using RetryOnceMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);
        TerrainTextureOverlay overlay = CreateFullCoverageOverlay("https://tiles.example/{z}/{x}/{y}.png");

        GeneratedTerrainTexture texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        Assert.Equal(512, texture.TextureImport.Width);
        Assert.Equal(5, handler.RequestCount);
    }

    private static TerrainTextureOverlay CreateFullCoverageOverlay(string urlTemplate, string? fallbackUrlTemplate = null)
    {
        return new TerrainTextureOverlay(
            PackageName: "dem",
            UrlTemplate: urlTemplate,
            ZoomLevel: 1,
            GeographicBounds: new GeographicRectangle(
                MinLatitude: 0.0,
                MaxLatitude: WebMercatorTileMath.MaxLatitude,
                MinLongitude: -180.0,
                MaxLongitude: 180.0),
            MaxTextureSize: 4096,
            FallbackUrlTemplate: fallbackUrlTemplate);
    }

    private static Image<Rgba32> LoadImage(ResoniteRawTextureImport texture)
    {
        return Image.LoadPixelData<Rgba32>(texture.RawRgba32Bytes, texture.Width, texture.Height);
    }
    private static bool ContainsColor(Image<Rgba32> image, byte expectedR, byte expectedG, byte expectedB)
    {
        bool found = false;

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height && !found; y++)
            {
                ReadOnlySpan<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    Rgba32 pixel = row[x];
                    if (pixel.R == expectedR && pixel.G == expectedG && pixel.B == expectedB)
                    {
                        found = true;
                        break;
                    }
                }
            }
        });

        return found;
    }

    private static void AssertColor(Rgba32 color, byte expectedR, byte expectedG, byte expectedB)
    {
        Assert.Equal(expectedR, color.R);
        Assert.Equal(expectedG, color.G);
        Assert.Equal(expectedB, color.B);
        Assert.Equal(byte.MaxValue, color.A);
    }

    private static int RoundUpToPowerOfTwo(int value)
    {
        int rounded = 1;
        while (rounded < value)
        {
            rounded <<= 1;
        }

        return rounded;
    }

    private sealed class FakeMapTileHandler(TimeSpan? delayPerRequest = null) : HttpMessageHandler
    {
        private int requestCount;

        public int RequestCount => requestCount;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref requestCount);
            if (delayPerRequest is { } delay)
            {
                await Task.Delay(delay, cancellationToken);
            }

            string[] segments = request.RequestUri!.AbsolutePath.Trim('/').Split('/');
            int tileX = int.Parse(segments[^2], CultureInfo.InvariantCulture);
            int tileY = int.Parse(Path.GetFileNameWithoutExtension(segments[^1]), CultureInfo.InvariantCulture);

            using Image<Rgba32> image = new(WebMercatorTileMath.TileSizePixels, WebMercatorTileMath.TileSizePixels, GetTileColorForTests(tileX, tileY));
            MemoryStream stream = new();
            await image.SaveAsPngAsync(stream, cancellationToken);
            stream.Position = 0;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        }

        internal static Rgba32 GetTileColorForTests(int tileX, int tileY)
        {
            return (tileX, tileY) switch
            {
                (0, 0) => new Rgba32(255, 0, 0, 255),
                (1, 0) => new Rgba32(0, 255, 0, 255),
                (0, 1) => new Rgba32(0, 0, 255, 255),
                (1, 1) => new Rgba32(255, 255, 0, 255),
                _ => new Rgba32(255, 0, 255, 255),
            };
        }
    }

    private sealed class FlakyMapTileHandler : HttpMessageHandler
    {
        private int requestCount;

        public int RequestCount => requestCount;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int currentRequest = Interlocked.Increment(ref requestCount);
            if (currentRequest == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            string[] segments = request.RequestUri!.AbsolutePath.Trim('/').Split('/');
            int tileX = int.Parse(segments[^2], CultureInfo.InvariantCulture);
            int tileY = int.Parse(Path.GetFileNameWithoutExtension(segments[^1]), CultureInfo.InvariantCulture);

            using Image<Rgba32> image = new(WebMercatorTileMath.TileSizePixels, WebMercatorTileMath.TileSizePixels, FakeMapTileHandler.GetTileColorForTests(tileX, tileY));
            MemoryStream stream = new();
            await image.SaveAsPngAsync(stream, cancellationToken);
            stream.Position = 0;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        }
    }

    private sealed class RetryOnceMapTileHandler : HttpMessageHandler
    {
        private int requestCount;

        public int RequestCount => requestCount;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int currentRequest = Interlocked.Increment(ref requestCount);
            if (currentRequest == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            string[] segments = request.RequestUri!.AbsolutePath.Trim('/').Split('/');
            int tileX = int.Parse(segments[^2], CultureInfo.InvariantCulture);
            int tileY = int.Parse(Path.GetFileNameWithoutExtension(segments[^1]), CultureInfo.InvariantCulture);

            using Image<Rgba32> image = new(WebMercatorTileMath.TileSizePixels, WebMercatorTileMath.TileSizePixels, FakeMapTileHandler.GetTileColorForTests(tileX, tileY));
            MemoryStream stream = new();
            await image.SaveAsPngAsync(stream, cancellationToken);
            stream.Position = 0;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        }
    }

    private sealed class PartialSourceFallbackMapTileHandler((int x, int y) missingPrimaryTile) : HttpMessageHandler
    {
        private readonly HashSet<(int X, int Y)> missingPrimaryTiles = [missingPrimaryTile];
        public List<string> RequestedHosts { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] segments = request.RequestUri!.AbsolutePath.Trim('/').Split('/');
            int tileX = int.Parse(segments[^2], CultureInfo.InvariantCulture);
            int tileY = int.Parse(Path.GetFileNameWithoutExtension(segments[^1]), CultureInfo.InvariantCulture);
            RequestedHosts.Add(request.RequestUri.Host);

            if (string.Equals(request.RequestUri.Host, "primary.example", StringComparison.Ordinal)
                && missingPrimaryTiles.Contains((tileX, tileY)))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            using Image<Rgba32> image = new(
                WebMercatorTileMath.TileSizePixels,
                WebMercatorTileMath.TileSizePixels,
                GetTileColorForRequest(request.RequestUri.Host, tileX, tileY));
            MemoryStream stream = new();
            await image.SaveAsPngAsync(stream, cancellationToken);
            stream.Position = 0;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(stream),
            };
        }

        private static Rgba32 GetTileColorForRequest(string host, int tileX, int tileY)
        {
            if (string.Equals(host, "fallback.example", StringComparison.Ordinal))
            {
                return new Rgba32(255, 0, 255, byte.MaxValue);
            }

            return FakeMapTileHandler.GetTileColorForTests(tileX, tileY);
        }
    }

    private sealed class PrimaryFallbackMapTileHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(request.RequestUri?.Host, "primary.example", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            string[] segments = request.RequestUri!.AbsolutePath.Trim('/').Split('/');
            int tileX = int.Parse(segments[^2], CultureInfo.InvariantCulture);
            int tileY = int.Parse(Path.GetFileNameWithoutExtension(segments[^1]), CultureInfo.InvariantCulture);

            using Image<Rgba32> image = new(WebMercatorTileMath.TileSizePixels, WebMercatorTileMath.TileSizePixels, FakeMapTileHandler.GetTileColorForTests(tileX, tileY));
            MemoryStream stream = new();
            await image.SaveAsPngAsync(stream, cancellationToken);
            stream.Position = 0;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        }
    }

    private sealed class RetryableMapTileHandler : HttpMessageHandler
    {
        private int requestCount;

        public int RequestCount => requestCount;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref requestCount);

            string[] segments = request.RequestUri!.AbsolutePath.Trim('/').Split('/');
            int tileX = int.Parse(segments[^2], CultureInfo.InvariantCulture);
            int tileY = int.Parse(Path.GetFileNameWithoutExtension(segments[^1]), CultureInfo.InvariantCulture);

            using Image<Rgba32> image = new(WebMercatorTileMath.TileSizePixels, WebMercatorTileMath.TileSizePixels, FakeMapTileHandler.GetTileColorForTests(tileX, tileY));
            MemoryStream stream = new();
            await image.SaveAsPngAsync(stream, cancellationToken);
            stream.Position = 0;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        }
    }

    private sealed class ZoomAwareFallbackMapTileHandler : HttpMessageHandler
    {
        public List<(string Host, int ZoomLevel, int TileX, int TileY)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] segments = request.RequestUri!.AbsolutePath.Trim('/').Split('/');
            int zoomLevel = int.Parse(segments[^3], CultureInfo.InvariantCulture);
            int tileX = int.Parse(segments[^2], CultureInfo.InvariantCulture);
            int tileY = int.Parse(Path.GetFileNameWithoutExtension(segments[^1]), CultureInfo.InvariantCulture);
            Requests.Add((request.RequestUri.Host, zoomLevel, tileX, tileY));

            if (string.Equals(request.RequestUri.Host, "primary.example", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            using Image<Rgba32> image = new(WebMercatorTileMath.TileSizePixels, WebMercatorTileMath.TileSizePixels, FakeMapTileHandler.GetTileColorForTests(tileX, tileY));
            MemoryStream stream = new();
            await image.SaveAsPngAsync(stream, cancellationToken);
            stream.Position = 0;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        }
    }

    private sealed class SecondaryPrimaryFallbackMapTileHandler : HttpMessageHandler
    {
        public List<(string Host, int ZoomLevel)> HostZoomRequests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] segments = request.RequestUri!.AbsolutePath.Trim('/').Split('/');
            int zoomLevel = int.Parse(segments[^3], CultureInfo.InvariantCulture);
            int tileX = int.Parse(segments[^2], CultureInfo.InvariantCulture);
            int tileY = int.Parse(Path.GetFileNameWithoutExtension(segments[^1]), CultureInfo.InvariantCulture);
            HostZoomRequests.Add((request.RequestUri.Host, zoomLevel));

            if (string.Equals(request.RequestUri.Host, "primary.example", StringComparison.Ordinal)
                && zoomLevel == 2)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (string.Equals(request.RequestUri.Host, "fallback.example", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            using Image<Rgba32> image = new(WebMercatorTileMath.TileSizePixels, WebMercatorTileMath.TileSizePixels, FakeMapTileHandler.GetTileColorForTests(tileX, tileY));
            MemoryStream stream = new();
            await image.SaveAsPngAsync(stream, cancellationToken);
            stream.Position = 0;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        }
    }
}
