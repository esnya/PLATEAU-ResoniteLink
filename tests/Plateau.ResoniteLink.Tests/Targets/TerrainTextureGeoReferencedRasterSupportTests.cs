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
    public void TryCreateMetadataResolvesWebMercatorBounds()
    {
        const double westLongitude = 139.0;
        const double eastLongitude = 139.01;
        const double southLatitude = 35.71;
        const double northLatitude = 35.72;
        double west = ToWebMercatorX(westLongitude);
        double east = ToWebMercatorX(eastLongitude);
        double south = ToWebMercatorY(southLatitude);
        double north = ToWebMercatorY(northLatitude);
        ushort[] geoKeyDirectory =
        [
            1, 1, 0, 1,
            3072, 0, 1, 3857,
        ];

        GeoReferencedRasterMetadata? metadata = TerrainTextureGeoReferencedRasterMetadataReader.TryCreateMetadata(
            pixelWidth: 100,
            pixelHeight: 100,
            modelTiePoint: [0.0, 0.0, 0.0, west, north, 0.0],
            pixelScale: [(east - west) / 100.0, (north - south) / 100.0, 0.0],
            modelTransform: null,
            geoKeyDirectory: geoKeyDirectory,
            geoDoubleParams: null,
            geoAsciiParams: "WGS 84 / Pseudo-Mercator|WGS 84|");

        Assert.NotNull(metadata);
        Assert.True(metadata.IsUsable);
        Assert.Equal("EPSG:3857", metadata.CoordinateSystemIdentifier);
        Assert.InRange(metadata.GeographicBounds.MinLatitude, 35.71, 35.72);
        Assert.InRange(metadata.GeographicBounds.MaxLatitude, 35.71, 35.73);
        Assert.InRange(metadata.GeographicBounds.MinLongitude, 138.99, 139.01);
        Assert.InRange(metadata.GeographicBounds.MaxLongitude, 139.00, 139.02);
        Assert.InRange(metadata.PixelWidthMeters, 11.0, 12.0);
        Assert.InRange(metadata.PixelHeightMeters, 13.0, 15.0);
    }

    [Fact]
    public void TryCreateMetadataResolvesUserDefinedPseudoMercatorFromAsciiCitation()
    {
        const double westLongitude = 139.0;
        const double eastLongitude = 139.01;
        const double southLatitude = 35.71;
        const double northLatitude = 35.72;
        double west = ToWebMercatorX(westLongitude);
        double east = ToWebMercatorX(eastLongitude);
        double south = ToWebMercatorY(southLatitude);
        double north = ToWebMercatorY(northLatitude);
        ushort[] geoKeyDirectory =
        [
            1, 1, 0, 1,
            3072, 0, 1, 32767,
        ];

        GeoReferencedRasterMetadata? metadata = TerrainTextureGeoReferencedRasterMetadataReader.TryCreateMetadata(
            pixelWidth: 100,
            pixelHeight: 100,
            modelTiePoint: [0.0, 0.0, 0.0, west, north, 0.0],
            pixelScale: [(east - west) / 100.0, (north - south) / 100.0, 0.0],
            modelTransform: null,
            geoKeyDirectory: geoKeyDirectory,
            geoDoubleParams: null,
            geoAsciiParams: "WGS 84 / Pseudo-Mercator|WGS 84|");

        Assert.NotNull(metadata);
        Assert.True(metadata.IsUsable);
        Assert.Equal("EPSG:3857", metadata.CoordinateSystemIdentifier);
    }

    [Fact]
    public void TryCreateMetadataResolvesUserDefinedJapanPlaneCitation()
    {
        ushort[] geoKeyDirectory =
        [
            1, 1, 0, 1,
            3072, 0, 1, 32767,
        ];

        GeoReferencedRasterMetadata? metadata = TerrainTextureGeoReferencedRasterMetadataReader.TryCreateMetadata(
            pixelWidth: 100,
            pixelHeight: 50,
            modelTiePoint: [0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
            pixelScale: [1.0, 1.0, 0.0],
            modelTransform: null,
            geoKeyDirectory: geoKeyDirectory,
            geoDoubleParams: null,
            geoAsciiParams: "JGD2011 / Japan Plane Rectangular CS IX|");

        Assert.NotNull(metadata);
        Assert.True(metadata.IsUsable);
        Assert.Equal("EPSG:6677", metadata.CoordinateSystemIdentifier);
    }

    private static double ToWebMercatorX(double longitude)
    {
        return (WebMercatorTileMath.LongitudeToNormalizedX(longitude) - 0.5) * (2.0 * Math.PI * 6_378_137.0);
    }

    private static double ToWebMercatorY(double latitude)
    {
        return (0.5 - WebMercatorTileMath.LatitudeToNormalizedY(latitude)) * (2.0 * Math.PI * 6_378_137.0);
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

        ResoniteRawTextureImport texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        Assert.Equal(0, handler.RequestCount);
        using Image<Rgba32> outputImage = Image.LoadPixelData<Rgba32>(texture.RawRgba32Bytes, texture.Width, texture.Height);
        Assert.Equal(new Rgba32(12, 34, 56, 255), outputImage[0, 0]);
        Assert.Contains("georaster|", texture.Identity, StringComparison.Ordinal);
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

        ResoniteRawTextureImport texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        Assert.Equal(4, handler.RequestCount);
        Assert.Contains("tile|1|https://tiles.example/{z}/{x}/{y}.png", texture.Identity, StringComparison.Ordinal);
    }

    [Fact]
    public void TryCropUsesMercatorVerticalInterpolationForEpsg3857()
    {
        GeographicRectangle rasterBounds = new(35.0, 36.0, 139.0, 140.0);
        GeographicRectangle requestedBounds = new(35.2, 35.4, 139.2, 139.4);
        GeoReferencedRasterMetadata metadata = new(rasterBounds, "EPSG:3857", 10.0, 10.0);
        using Image<Rgba32> raster = new(10, 100);
        for (int y = 0; y < raster.Height; y++)
        {
            raster.ProcessPixelRows(accessor =>
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                row.Fill(new Rgba32((byte)y, 0, 0, 255));
            });
        }

        using Image<Rgba32>? cropped = TerrainTextureGeoReferencedRasterCropper.TryCrop(raster, metadata, requestedBounds);

        Assert.NotNull(cropped);
        double rasterTop = WebMercatorTileMath.LatitudeToNormalizedY(rasterBounds.MaxLatitude);
        double rasterBottom = WebMercatorTileMath.LatitudeToNormalizedY(rasterBounds.MinLatitude);
        double requestTop = WebMercatorTileMath.LatitudeToNormalizedY(requestedBounds.MaxLatitude);
        int expectedTop = (int)Math.Floor(((requestTop - rasterTop) / (rasterBottom - rasterTop)) * raster.Height);
        Assert.Equal((byte)expectedTop, cropped![0, 0].R);
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
