using System.Globalization;
using System.Net;

using Plateau.ResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Plateau.ResoniteLink.Tests.Targets;

public sealed class TerrainTextureAssetGeneratorTests
{
    [Fact]
    public async Task EnsureTextureAsyncStitchesTilesAndCachesOutput()
    {
        using FakeMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);

        TerrainTextureOverlay overlay = CreateFullCoverageOverlay("https://tiles.example/{z}/{x}/{y}.png");

        ResoniteRawTextureImport firstTexture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);
        ResoniteRawTextureImport secondTexture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        Assert.Same(firstTexture, secondTexture);
        Assert.Contains("terrain-overlay/dem/tile|1|https://tiles.example/{z}/{x}/{y}.png/", firstTexture.Identity, StringComparison.Ordinal);

        using Image<Rgba32> image = Image.LoadPixelData<Rgba32>(firstTexture.RawRgba32Bytes, firstTexture.Width, firstTexture.Height);
        Assert.Equal(512, image.Width);
        Assert.InRange(image.Height, 256, 257);
        AssertColor(image[128, 128], 255, 0, 0);
        AssertColor(image[384, 128], 0, 255, 0);
    }

    [Fact]
    public async Task EnsureTextureAsyncResizesWhenCroppedTextureExceedsMaxTextureSize()
    {
        using FakeMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);

        TerrainTextureOverlay overlay = CreateFullCoverageOverlay("https://tiles.example/{z}/{x}/{y}.png") with
        {
            MaxTextureSize = 256,
        };

        ResoniteRawTextureImport texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        using Image<Rgba32> image = Image.LoadPixelData<Rgba32>(texture.RawRgba32Bytes, texture.Width, texture.Height);
        Assert.Equal(256, image.Width);
        Assert.InRange(image.Height, 127, 128);
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

        ResoniteRawTextureImport texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        using Image<Rgba32> image = Image.LoadPixelData<Rgba32>(texture.RawRgba32Bytes, texture.Width, texture.Height);
        Assert.InRange(image.Width, 512, 513);
        Assert.InRange(image.Height, 512, 513);
        AssertColor(image[image.Width / 4, image.Height / 4], 255, 0, 0);
        AssertColor(image[(image.Width * 3) / 4, image.Height / 4], 0, 255, 0);
        AssertColor(image[image.Width / 4, (image.Height * 3) / 4], 0, 0, 255);
        AssertColor(image[(image.Width * 3) / 4, (image.Height * 3) / 4], 255, 255, 0);
    }

    [Fact]
    public async Task EnsureTextureAsyncSharesConcurrentRequestsForTheSameOverlay()
    {
        using FakeMapTileHandler handler = new(delayPerRequest: TimeSpan.FromMilliseconds(50));
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);
        TerrainTextureOverlay overlay = CreateFullCoverageOverlay("https://tiles.example/{z}/{x}/{y}.png");

        Task<ResoniteRawTextureImport>[] requests =
        [
            generator.EnsureTextureAsync(overlay, CancellationToken.None),
            generator.EnsureTextureAsync(overlay, CancellationToken.None),
            generator.EnsureTextureAsync(overlay, CancellationToken.None),
        ];

        ResoniteRawTextureImport[] textures = await Task.WhenAll(requests);

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

        ResoniteRawTextureImport texture = await secondGenerator.EnsureTextureAsync(overlay, CancellationToken.None);

        Assert.Equal(0, secondHandler.RequestCount);
        Assert.Contains("terrain-overlay/dem/tile|1|https://tiles.example/{z}/{x}/{y}.png/", texture.Identity, StringComparison.Ordinal);
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

        ResoniteRawTextureImport texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        Assert.Equal(512, texture.Width);
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

        ResoniteRawTextureImport texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        Assert.Equal(512, texture.Width);
        Assert.Equal(5, handler.RequestCount);
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

        ResoniteRawTextureImport texture = await secondGenerator.EnsureTextureAsync(overlay, CancellationToken.None);

        Assert.Equal(512, texture.Width);
        Assert.Equal(4, secondHandler.RequestCount);
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

        ResoniteRawTextureImport texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        Assert.Equal(512, texture.Width);
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

        ResoniteRawTextureImport texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        using Image<Rgba32> image = Image.LoadPixelData<Rgba32>(texture.RawRgba32Bytes, texture.Width, texture.Height);
        AssertColor(image[128, 128], 255, 0, 0);
        AssertColor(image[384, 128], 0, 255, 0);
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

    private static void AssertColor(Rgba32 color, byte expectedR, byte expectedG, byte expectedB)
    {
        Assert.Equal(expectedR, color.R);
        Assert.Equal(expectedG, color.G);
        Assert.Equal(expectedB, color.B);
        Assert.Equal(byte.MaxValue, color.A);
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
}
