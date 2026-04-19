using System.Net;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Plateau.ResoniteLink.Tests.Targets;

public sealed class TerrainTextureGeoReferencedRasterSupportTests
{
    [Fact]
    public void TryCreateMetadataResolvesJapanPlaneRectangularBounds()
    {
        ushort[] geoKeyDirectory =
        [
            1, 1, 0, 1,
            3072, 0, 1, 6676,
        ];

        GeoReferencedRasterMetadata? metadata = TerrainTextureGeoReferencedRasterMetadataReader.TryCreateMetadata(
            pixelWidth: 100,
            pixelHeight: 50,
            modelTiePoint: [0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
            pixelScale: [1.0, 1.0, 0.0],
            modelTransform: null,
            geoKeyDirectory: geoKeyDirectory,
            geoDoubleParams: null,
            geoAsciiParams: null);

        Assert.NotNull(metadata);
        Assert.True(metadata.IsUsable);
        Assert.Equal("EPSG:6676", metadata.CoordinateSystemIdentifier);
        Assert.Equal(1.0, metadata.PixelWidthMeters);
        Assert.Equal(1.0, metadata.PixelHeightMeters);
        Assert.InRange(metadata.GeographicBounds.MaxLatitude, 35.99, 36.01);
        Assert.InRange(metadata.GeographicBounds.MinLongitude, 138.49, 138.51);
        Assert.True(metadata.GeographicBounds.MaxLongitude > metadata.GeographicBounds.MinLongitude);
        Assert.True(metadata.GeographicBounds.MaxLatitude > metadata.GeographicBounds.MinLatitude);
    }

    [Fact]
    public void TryCreateMetadataReturnsUnusableMetadataWhenCoordinateSystemIsMissing()
    {
        GeoReferencedRasterMetadata? metadata = TerrainTextureGeoReferencedRasterMetadataReader.TryCreateMetadata(
            pixelWidth: 10,
            pixelHeight: 10,
            modelTiePoint: [0.0, 0.0, 0.0, 139.0, 35.0, 0.0],
            pixelScale: [0.00001, 0.00001, 0.0],
            modelTransform: null,
            geoKeyDirectory: null,
            geoDoubleParams: null,
            geoAsciiParams: null);

        Assert.NotNull(metadata);
        Assert.False(metadata.IsUsable);
        Assert.Null(metadata.CoordinateSystemIdentifier);
    }

    [Fact]
    public void TryCreateMetadataResolvesWebMercatorBoundsFromRealPlateauGeoTiffTags()
    {
        ushort[] geoKeyDirectory =
        [
            1, 1, 0, 7,
            1024, 0, 1, 1,
            1025, 0, 1, 1,
            1026, 34737, 25, 0,
            2049, 34737, 7, 25,
            2054, 0, 1, 9102,
            3072, 0, 1, 3857,
            3076, 0, 1, 9001,
        ];

        GeoReferencedRasterMetadata? metadata = TerrainTextureGeoReferencedRasterMetadataReader.TryCreateMetadata(
            pixelWidth: 2822,
            pixelHeight: 2318,
            modelTiePoint: [0.0, 0.0, 0.0, 15522111.49748708, 4269705.744087971, 0.0],
            pixelScale: [0.49308779137044045, 0.4932342623689913, 0.0],
            modelTransform: null,
            geoKeyDirectory: geoKeyDirectory,
            geoDoubleParams: null,
            geoAsciiParams: "WGS 84 / Pseudo-Mercator|WGS 84|");

        Assert.NotNull(metadata);
        Assert.True(metadata.IsUsable);
        Assert.Equal("EPSG:3857", metadata.CoordinateSystemIdentifier);
        Assert.InRange(metadata.GeographicBounds.MinLatitude, 35.76, 35.77);
        Assert.InRange(metadata.GeographicBounds.MaxLatitude, 35.77, 35.78);
        Assert.InRange(metadata.GeographicBounds.MinLongitude, 139.43, 139.45);
        Assert.InRange(metadata.GeographicBounds.MaxLongitude, 139.44, 139.46);
        Assert.True(metadata.PixelWidthMeters > 0.49);
        Assert.True(metadata.PixelHeightMeters > 0.49);
    }

    [Fact]
    public async Task EnsureTextureAsyncUsesGeoReferencedRasterSourceBeforeTileFallback()
    {
        using TemporaryDirectory workDirectory = new();
        string rasterPath = Path.Combine(workDirectory.Path, "terrain.png");
        using (Image<Rgba32> rasterImage = new(4, 4, new Rgba32(12, 34, 56, 255)))
        {
            await rasterImage.SaveAsPngAsync(rasterPath);
        }

        GeographicRectangle bounds = new(35.0, 35.001, 139.0, 139.001);
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            GeographicBounds: bounds,
            MaxTextureSize: 1024,
            Sources:
            [
                new TerrainTextureGeoReferencedRasterSource(
                    rasterPath,
                    new GeoReferencedRasterMetadata(bounds, "EPSG:4326", 10.0, 10.0)),
                new TerrainTextureTileSource("https://tiles.example/{z}/{x}/{y}.png", 18),
            ]);

        using NeverCalledMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);

        GeneratedTerrainTexture texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        Assert.Equal(0, handler.RequestCount);
        using Image<Rgba32> outputImage = Image.LoadPixelData<Rgba32>(
            texture.TextureImport.RawRgba32Bytes,
            texture.TextureImport.Width,
            texture.TextureImport.Height);
        Assert.Equal(new Rgba32(12, 34, 56, 255), outputImage[0, 0]);
        Assert.Contains("georaster|", texture.TextureImport.Identity, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnsureTextureAsyncKeepsDefaultThirdMeshDemOverlayPixelPerfectWithinLargeBudget()
    {
        MeshCodeBounds meshBounds = MeshCodeBounds.TryParse("54372778")
            ?? throw new InvalidOperationException("Expected Matsumoto third mesh bounds.");
        TerrainTextureOverlay tileOverlay = Assert.Single(
            LocalCityGmlDemBootstrapSupport.CreateDemTerrainTextureOverlays(
                new DemTerrainBounds(
                    meshBounds.SouthLatitude,
                    meshBounds.NorthLatitude,
                    meshBounds.WestLongitude,
                    meshBounds.EastLongitude),
                ["54372778"]));
        TerrainTextureLayoutPlan layout = TerrainTextureLayoutPlanner.Create(
            tileOverlay.GeographicBounds,
            tileOverlay.ZoomLevel);

        using TemporaryDirectory workDirectory = new();
        string rasterPath = Path.Combine(workDirectory.Path, "default-third-mesh-dem.png");
        using (Image<Rgba32> rasterImage = new(layout.CropWidth, layout.CropHeight, new Rgba32(12, 34, 56, 255)))
        {
            await rasterImage.SaveAsPngAsync(rasterPath);
        }

        TerrainTextureOverlay rasterOverlay = tileOverlay with
        {
            Sources =
            [
                new TerrainTextureGeoReferencedRasterSource(
                    rasterPath,
                    new GeoReferencedRasterMetadata(tileOverlay.GeographicBounds, "EPSG:4326", 1.0, 1.0)),
            ],
        };

        using NeverCalledMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);

        GeneratedTerrainTexture texture = await generator.EnsureTextureAsync(rasterOverlay, CancellationToken.None);

        Assert.Equal(0, handler.RequestCount);
        Assert.Equal(8192, texture.TextureImport.Width);
        Assert.Equal(4096, texture.TextureImport.Height);
        Assert.Equal(
            new ResoniteFloat2(
                (double)layout.CropWidth / texture.TextureImport.Width,
                (double)layout.CropHeight / texture.TextureImport.Height),
            texture.CanvasScale);
        using Image<Rgba32> outputImage = Image.LoadPixelData<Rgba32>(
            texture.TextureImport.RawRgba32Bytes,
            texture.TextureImport.Width,
            texture.TextureImport.Height);
        int occupiedTop = outputImage.Height - layout.CropHeight;
        Assert.Equal(TerrainTextureAssetGenerator.DefaultDemGroundFillColor, outputImage[0, 0]);
        Assert.Equal(new Rgba32(12, 34, 56, 255), outputImage[0, occupiedTop]);
    }

    [Fact]
    public async Task EnsureTextureAsyncFlattensTransparentGeoReferencedRasterPixelsToGroundColor()
    {
        using TemporaryDirectory workDirectory = new();
        string rasterPath = Path.Combine(workDirectory.Path, "terrain.png");
        using (Image<Rgba32> rasterImage = new(2, 2))
        {
            rasterImage[0, 0] = new Rgba32(0, 0, 0, 0);
            rasterImage[1, 0] = new Rgba32(12, 34, 56, 255);
            rasterImage[0, 1] = new Rgba32(0, 0, 0, 0);
            rasterImage[1, 1] = new Rgba32(78, 90, 12, 255);
            await rasterImage.SaveAsPngAsync(rasterPath);
        }

        GeographicRectangle bounds = new(35.0, 35.001, 139.0, 139.001);
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            GeographicBounds: bounds,
            MaxTextureSize: 16,
            Sources:
            [
                new TerrainTextureGeoReferencedRasterSource(
                    rasterPath,
                    new GeoReferencedRasterMetadata(bounds, "EPSG:4326", 10.0, 10.0)),
            ]);

        TerrainTextureAssetGenerator generator = new(disablePersistentCache: true);

        GeneratedTerrainTexture texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        using Image<Rgba32> outputImage = Image.LoadPixelData<Rgba32>(
            texture.TextureImport.RawRgba32Bytes,
            texture.TextureImport.Width,
            texture.TextureImport.Height);
        Assert.Equal(TerrainTextureAssetGenerator.DefaultDemGroundFillColor, outputImage[0, 0]);
        Assert.Equal(new Rgba32(12, 34, 56, 255), outputImage[1, 0]);
        Assert.Equal(TerrainTextureAssetGenerator.DefaultDemGroundFillColor, outputImage[0, 1]);
        Assert.Equal(new Rgba32(78, 90, 12, 255), outputImage[1, 1]);
    }

    [Fact]
    public async Task EnsureTextureAsyncSkipsUnsupportedGeoReferencedRasterSource()
    {
        GeographicRectangle bounds = new(0.0, WebMercatorTileMath.MaxLatitude, -180.0, 180.0);
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            GeographicBounds: bounds,
            MaxTextureSize: 1024,
            Sources:
            [
                new TerrainTextureGeoReferencedRasterSource("missing.tif"),
                new TerrainTextureTileSource("https://tiles.example/{z}/{x}/{y}.png", 1),
            ]);

        using TerrainTextureAssetGeneratorTestsProxyMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);

        GeneratedTerrainTexture texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        Assert.Equal(4, handler.RequestCount);
        Assert.Contains("tile|1|https://tiles.example/{z}/{x}/{y}.png", texture.TextureImport.Identity, StringComparison.Ordinal);
    }

    private sealed class NeverCalledMapTileHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            throw new InvalidOperationException($"Unexpected tile request to '{request.RequestUri}'.");
        }
    }

    private sealed class TerrainTextureAssetGeneratorTestsProxyMapTileHandler : HttpMessageHandler
    {
        private int requestCount;

        public int RequestCount => requestCount;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref requestCount);

            string[] segments = request.RequestUri!.AbsolutePath.Trim('/').Split('/');
            int tileX = int.Parse(segments[^2], System.Globalization.CultureInfo.InvariantCulture);
            int tileY = int.Parse(Path.GetFileNameWithoutExtension(segments[^1]), System.Globalization.CultureInfo.InvariantCulture);
            using Image<Rgba32> image = new(WebMercatorTileMath.TileSizePixels, WebMercatorTileMath.TileSizePixels, GetTileColor(tileX, tileY));
            MemoryStream stream = new();
            await image.SaveAsPngAsync(stream, cancellationToken);
            stream.Position = 0;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
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
