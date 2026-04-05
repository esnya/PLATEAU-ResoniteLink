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
        using TemporaryDirectory workRoot = new();
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

        string firstPath = await generator.EnsureTextureAsync(
            terrainTextureOverlay,
            workRoot.Path,
            CancellationToken.None);
        string secondPath = await generator.EnsureTextureAsync(
            terrainTextureOverlay,
            workRoot.Path,
            CancellationToken.None);

        Assert.Equal(firstPath, secondPath);
        Assert.True(File.Exists(firstPath));

        using Image<Rgba32> image = await Image.LoadAsync<Rgba32>(firstPath);
        Assert.Equal(512, image.Width);
        Assert.InRange(image.Height, 256, 257);
        AssertColor(image[128, 128], 255, 0, 0);
        AssertColor(image[384, 128], 0, 255, 0);
    }

    [Fact]
    public async Task EnsureTextureAsyncResizesWhenCroppedTextureExceedsMaxTextureSize()
    {
        using TemporaryDirectory workRoot = new();
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

        string texturePath = await generator.EnsureTextureAsync(
            terrainTextureOverlay,
            workRoot.Path,
            CancellationToken.None);

        using Image<Rgba32> image = await Image.LoadAsync<Rgba32>(texturePath);
        Assert.Equal(256, image.Width);
        Assert.InRange(image.Height, 127, 128);
    }

    private static void AssertColor(Rgba32 color, byte expectedR, byte expectedG, byte expectedB)
    {
        Assert.Equal(expectedR, color.R);
        Assert.Equal(expectedG, color.G);
        Assert.Equal(expectedB, color.B);
        Assert.Equal(byte.MaxValue, color.A);
    }

    private sealed class FakeMapTileHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string[] segments = request.RequestUri!.AbsolutePath.Trim('/').Split('/');
            int tileX = int.Parse(segments[^2], CultureInfo.InvariantCulture);

            using Image<Rgba32> image = new(
                WebMercatorTileMath.TileSizePixels,
                WebMercatorTileMath.TileSizePixels,
                tileX == 0 ? new Rgba32(255, 0, 0, 255) : new Rgba32(0, 255, 0, 255));
            MemoryStream stream = new();
            await image.SaveAsPngAsync(stream, cancellationToken);
            stream.Position = 0;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(stream),
            };
        }
    }
}
