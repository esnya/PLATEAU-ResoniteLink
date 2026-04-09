using System.Globalization;
using System.Net;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class TerrainTextureAssetGeneratorTests
{
    [Fact]
    public async Task EnsureTextureAsyncStitchesTilesAndCachesOutput()
    {
        using FakeMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient);

        TerrainTextureOverlay terrainTextureOverlay = new(
            TexturePath: LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTexturePath,
            PackageName: "dem",
            UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
            ZoomLevel: 1,
            GeographicBounds: new GeographicRectangle(
                MinLatitude: 0.0,
                MaxLatitude: WebMercatorTileMath.MaxLatitude,
                MinLongitude: -180.0,
                MaxLongitude: 180.0),
            MaxTextureSize: LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureMaxSize);

        ResoniteRawTextureImport firstTexture = await generator.EnsureTextureAsync(
            terrainTextureOverlay,
            CancellationToken.None);
        ResoniteRawTextureImport secondTexture = await generator.EnsureTextureAsync(
            terrainTextureOverlay,
            CancellationToken.None);

        Assert.Same(firstTexture, secondTexture);

        using Image<Rgba32> image = Image.LoadPixelData<Rgba32>(
            firstTexture.RawRgba32Bytes,
            firstTexture.Width,
            firstTexture.Height);
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
        TerrainTextureAssetGenerator generator = new(httpClient);

        TerrainTextureOverlay terrainTextureOverlay = new(
            TexturePath: LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTexturePath,
            PackageName: "dem",
            UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
            ZoomLevel: 1,
            GeographicBounds: new GeographicRectangle(
                MinLatitude: 0.0,
                MaxLatitude: WebMercatorTileMath.MaxLatitude,
                MinLongitude: -180.0,
                MaxLongitude: 180.0),
            MaxTextureSize: 256);

        ResoniteRawTextureImport texture = await generator.EnsureTextureAsync(
            terrainTextureOverlay,
            CancellationToken.None);

        using Image<Rgba32> image = Image.LoadPixelData<Rgba32>(
            texture.RawRgba32Bytes,
            texture.Width,
            texture.Height);
        Assert.Equal(256, image.Width);
        Assert.InRange(image.Height, 127, 128);
    }

    [Fact]
    public async Task EnsureTextureAsyncKeepsNorthUpOrientationAcrossStitchedTiles()
    {
        using FakeMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient);

        TerrainTextureOverlay terrainTextureOverlay = new(
            TexturePath: LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTexturePath,
            PackageName: "dem",
            UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
            ZoomLevel: 1,
            GeographicBounds: new GeographicRectangle(
                MinLatitude: -WebMercatorTileMath.MaxLatitude,
                MaxLatitude: WebMercatorTileMath.MaxLatitude,
                MinLongitude: -180.0,
                MaxLongitude: 180.0),
            MaxTextureSize: LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureMaxSize);

        ResoniteRawTextureImport texture = await generator.EnsureTextureAsync(
            terrainTextureOverlay,
            CancellationToken.None);

        using Image<Rgba32> image = Image.LoadPixelData<Rgba32>(
            texture.RawRgba32Bytes,
            texture.Width,
            texture.Height);

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
        TerrainTextureAssetGenerator generator = new(httpClient);

        TerrainTextureOverlay terrainTextureOverlay = new(
            TexturePath: LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTexturePath,
            PackageName: "dem",
            UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
            ZoomLevel: 1,
            GeographicBounds: new GeographicRectangle(
                MinLatitude: 0.0,
                MaxLatitude: WebMercatorTileMath.MaxLatitude,
                MinLongitude: -180.0,
                MaxLongitude: 180.0),
            MaxTextureSize: LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureMaxSize);

        Task<ResoniteRawTextureImport>[] requests =
        [
            generator.EnsureTextureAsync(terrainTextureOverlay, CancellationToken.None),
            generator.EnsureTextureAsync(terrainTextureOverlay, CancellationToken.None),
            generator.EnsureTextureAsync(terrainTextureOverlay, CancellationToken.None),
        ];

        ResoniteRawTextureImport[] textures = await Task.WhenAll(requests);

        Assert.All(textures, texture => Assert.Same(textures[0], texture));
        Assert.Equal(4, handler.RequestCount);
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

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
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

            using Image<Rgba32> image = new(
                WebMercatorTileMath.TileSizePixels,
                WebMercatorTileMath.TileSizePixels,
                GetTileColor(tileX, tileY));
            MemoryStream stream = new();
            await image.SaveAsPngAsync(stream, cancellationToken);
            stream.Position = 0;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(stream),
            };
        }

        private static Rgba32 GetTileColor(int tileX, int tileY)
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
}
