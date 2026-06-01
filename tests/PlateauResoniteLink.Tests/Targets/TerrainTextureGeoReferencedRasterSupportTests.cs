using System;
using System.Buffers.Binary;
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
        Assert.Equal("EPSG:6676", metadata.CoordinateSystemIdentifier);
        Assert.Equal(1.0, metadata.PixelWidthMeters);
        Assert.Equal(1.0, metadata.PixelHeightMeters);
        Assert.InRange(metadata.GeographicBounds.MaxLatitude, 35.99, 36.01);
        Assert.InRange(metadata.GeographicBounds.MinLongitude, 138.49, 138.51);
        Assert.True(metadata.GeographicBounds.MaxLongitude > metadata.GeographicBounds.MinLongitude);
        Assert.True(metadata.GeographicBounds.MaxLatitude > metadata.GeographicBounds.MinLatitude);
    }

    [Fact]
    public void TryCreateMetadataReturnsNullWhenCoordinateSystemIsMissing()
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

        Assert.Null(metadata);
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
        Assert.Equal("EPSG:3857", metadata.CoordinateSystemIdentifier);
        Assert.InRange(metadata.GeographicBounds.MinLatitude, 35.76, 35.77);
        Assert.InRange(metadata.GeographicBounds.MaxLatitude, 35.77, 35.78);
        Assert.InRange(metadata.GeographicBounds.MinLongitude, 139.43, 139.45);
        Assert.InRange(metadata.GeographicBounds.MaxLongitude, 139.44, 139.46);
        Assert.True(metadata.PixelWidthMeters > 0.49);
        Assert.True(metadata.PixelHeightMeters > 0.49);
    }

    [Fact]
    public void GeoTiffTagReaderParsesGeoTiffTagsWithoutExifProfile()
    {
        byte[] bytes = CreateClassicLittleEndianGeoTiffBytes(
            modelTiePoint: [0.0, 0.0, 0.0, 139.0, 35.0, 0.0],
            pixelScale: [0.0001, 0.0001, 0.0],
            geoKeyDirectory:
            [
                1, 1, 0, 1,
                2048, 0, 1, 4326,
            ]);

        GeoTiffTagSnapshot snapshot = Assert.IsType<GeoTiffTagSnapshot>(GeoTiffTagReader.TryRead(bytes));
        GeoReferencedRasterMetadata metadata = Assert.IsType<GeoReferencedRasterMetadata>(
            TerrainTextureGeoReferencedRasterMetadataReader.TryCreateMetadata(
                pixelWidth: 10,
                pixelHeight: 10,
                modelTiePoint: snapshot.ModelTiePoint,
                pixelScale: snapshot.PixelScale,
                modelTransform: snapshot.ModelTransform,
                geoKeyDirectory: snapshot.GeoKeyDirectory,
                geoDoubleParams: snapshot.GeoDoubleParams,
                geoAsciiParams: snapshot.GeoAsciiParams));

        Assert.Equal("EPSG:4326", metadata.CoordinateSystemIdentifier);
        Assert.InRange(metadata.GeographicBounds.MinLatitude, 34.9989, 34.9991);
        Assert.InRange(metadata.GeographicBounds.MaxLongitude, 139.0009, 139.0011);
    }

    [Fact]
    public void TryCreateMetadataResolvesUserDefinedGeoKeyFromGeoAsciiParams()
    {
        ushort[] geoKeyDirectory =
        [
            1, 1, 0, 1,
            3072, 0, 1, 32767,
        ];

        GeoReferencedRasterMetadata? metadata = TerrainTextureGeoReferencedRasterMetadataReader.TryCreateMetadata(
            pixelWidth: 10,
            pixelHeight: 10,
            modelTiePoint: [0.0, 0.0, 0.0, 15522111.49748708, 4269705.744087971, 0.0],
            pixelScale: [0.49308779137044045, 0.4932342623689913, 0.0],
            modelTransform: null,
            geoKeyDirectory: geoKeyDirectory,
            geoDoubleParams: null,
            geoAsciiParams: "WGS 84 / Pseudo-Mercator|WGS 84|");

        Assert.NotNull(metadata);
        Assert.Equal("EPSG:3857", metadata.CoordinateSystemIdentifier);
    }

    [Fact]
    public void TryCreateMetadataFallsBackToGeoAsciiWhenGeoKeyDirectoryIsMissing()
    {
        GeoReferencedRasterMetadata? metadata = TerrainTextureGeoReferencedRasterMetadataReader.TryCreateMetadata(
            pixelWidth: 10,
            pixelHeight: 10,
            modelTiePoint: [0.0, 0.0, 0.0, 15522111.49748708, 4269705.744087971, 0.0],
            pixelScale: [0.49308779137044045, 0.4932342623689913, 0.0],
            modelTransform: null,
            geoKeyDirectory: null,
            geoDoubleParams: null,
            geoAsciiParams: "WGS 84 / Pseudo-Mercator|WGS 84|");

        Assert.NotNull(metadata);
        Assert.Equal("EPSG:3857", metadata.CoordinateSystemIdentifier);
    }

    [Fact]
    public async Task GeoTiffTagReaderTryReadAsyncParsesGeoTiffTagsFromFileStream()
    {
        using TemporaryDirectory workDirectory = new();
        string rasterPath = Path.Combine(workDirectory.Path, "geotiff-tags.tif");
        byte[] bytes = CreateClassicLittleEndianGeoTiffBytes(
            modelTiePoint: [0.0, 0.0, 0.0, 139.0, 35.0, 0.0],
            pixelScale: [0.0001, 0.0001, 0.0],
            geoKeyDirectory:
            [
                1, 1, 0, 1,
                2048, 0, 1, 4326,
            ]);
        await File.WriteAllBytesAsync(rasterPath, bytes);

        GeoTiffTagSnapshot snapshot = Assert.IsType<GeoTiffTagSnapshot>(
            await GeoTiffTagReader.TryReadAsync(rasterPath, CancellationToken.None));

        Assert.NotNull(snapshot.GeoKeyDirectory);
        Assert.Equal((ushort)4326, Assert.Single(snapshot.GeoKeyDirectory.Skip(7)));
        Assert.NotNull(snapshot.ModelTiePoint);
        Assert.NotNull(snapshot.PixelScale);
    }

    [Fact]
    public void TryCropUsesMercatorVerticalInterpolationForEpsg3857()
    {
        using Image<Rgba32> sourceImage = new(4, 4);
        for (int x = 0; x < sourceImage.Width; x++)
        {
            sourceImage[x, 0] = new Rgba32(255, 0, 0, 255);
            sourceImage[x, 1] = new Rgba32(0, 255, 0, 255);
            sourceImage[x, 2] = new Rgba32(0, 0, 255, 255);
            sourceImage[x, 3] = new Rgba32(255, 255, 0, 255);
        }

        GeographicRectangle rasterBounds = new(0.0, 80.0, 139.0, 140.0);
        double mercatorMidLatitude = Math.Atan(Math.Sinh((ToMercatorY(80.0) + ToMercatorY(0.0)) / 2.0)) * (180.0 / Math.PI);
        using Image<Rgba32> cropped = Assert.IsType<Image<Rgba32>>(TerrainTextureGeoReferencedRasterCropper.TryCrop(
            sourceImage,
            new GeoReferencedRasterMetadata(rasterBounds, "EPSG:3857", 1.0, 1.0),
            new GeographicRectangle(mercatorMidLatitude, 80.0, 139.0, 140.0)));

        Assert.Equal(4, cropped.Width);
        Assert.True(cropped.Height >= 2);
        Assert.Equal(new Rgba32(255, 0, 0, 255), cropped[0, 0]);
        Assert.Equal(new Rgba32(0, 255, 0, 255), cropped[0, 1]);
    }

    [Fact]
    public void TryCropReturnsOverlayAlignedCanvasForPartialGeoReferencedRasterCoverage()
    {
        using Image<Rgba32> sourceImage = new(2, 2, new Rgba32(255, 0, 0, 255));
        using Image<Rgba32> cropped = Assert.IsType<Image<Rgba32>>(TerrainTextureGeoReferencedRasterCropper.TryCrop(
            sourceImage,
            new GeoReferencedRasterMetadata(
                new GeographicRectangle(35.0, 35.01, 139.0, 139.01),
                "EPSG:4326",
                1.0,
                1.0),
            new GeographicRectangle(35.0, 35.01, 139.0, 139.02)));

        Assert.Equal(4, cropped.Width);
        Assert.Equal(2, cropped.Height);
        Assert.Equal(new Rgba32(255, 0, 0, 255), cropped[0, 0]);
        Assert.Equal(new Rgba32(255, 0, 0, 255), cropped[1, 0]);
        Assert.Equal(new Rgba32(0, 0, 0, 0), cropped[3, 0]);
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
            MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
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
            Materialize(texture.TextureSource).Bytes,
            Materialize(texture.TextureSource).Width,
            Materialize(texture.TextureSource).Height);
        Assert.Equal(new Rgba32(12, 34, 56, 255), outputImage[0, 0]);
        Assert.NotEmpty(Materialize(texture.TextureSource).Bytes);
        Assert.IsType<TerrainTextureGeoReferencedRasterSource>(texture.UsedSource);
    }

    [Fact]
    public async Task EnsureTextureAsyncDistinguishesTextureContentAcrossTileRasterAndMixedSources()
    {
        using TemporaryDirectory workDirectory = new();
        string rasterPath = Path.Combine(workDirectory.Path, "terrain.png");
        GeographicRectangle bounds = new(0.0, WebMercatorTileMath.MaxLatitude, -180.0, 180.0);
        using (Image<Rgba32> rasterImage = new(2, 2))
        {
            rasterImage[0, 0] = new Rgba32(12, 34, 56, 255);
            rasterImage[1, 0] = new Rgba32(0, 0, 0, 0);
            rasterImage[0, 1] = new Rgba32(12, 34, 56, 255);
            rasterImage[1, 1] = new Rgba32(0, 0, 0, 0);
            await rasterImage.SaveAsPngAsync(rasterPath);
        }

        TerrainTextureOverlay tileOnlyOverlay = new(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
            GeographicBounds: bounds,
            MaxTextureSize: 1024,
            Sources:
            [
                new TerrainTextureTileSource("https://tiles.example/{z}/{x}/{y}.png", 1),
            ]);
        TerrainTextureOverlay rasterOnlyOverlay = new(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
            GeographicBounds: bounds,
            MaxTextureSize: 1024,
            Sources:
            [
                new TerrainTextureGeoReferencedRasterSource(
                    rasterPath,
                    new GeoReferencedRasterMetadata(bounds, "EPSG:4326", 1.0, 1.0)),
            ]);
        TerrainTextureOverlay mixedOverlay = new(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
            GeographicBounds: bounds,
            MaxTextureSize: 1024,
            Sources:
            [
                new TerrainTextureGeoReferencedRasterSource(
                    rasterPath,
                    new GeoReferencedRasterMetadata(bounds, "EPSG:4326", 1.0, 1.0)),
                new TerrainTextureTileSource("https://tiles.example/{z}/{x}/{y}.png", 1),
            ]);

        using TerrainTextureAssetGeneratorTestsProxyMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);

        GeneratedTerrainTexture tileOnlyTexture = await generator.EnsureTextureAsync(tileOnlyOverlay, CancellationToken.None);
        GeneratedTerrainTexture rasterOnlyTexture = await generator.EnsureTextureAsync(rasterOnlyOverlay, CancellationToken.None);
        GeneratedTerrainTexture mixedTexture = await generator.EnsureTextureAsync(mixedOverlay, CancellationToken.None);

        Assert.NotEqual(Materialize(tileOnlyTexture.TextureSource).Bytes, Materialize(rasterOnlyTexture.TextureSource).Bytes);
        Assert.NotEqual(Materialize(tileOnlyTexture.TextureSource).Bytes, Materialize(mixedTexture.TextureSource).Bytes);
        Assert.NotEqual(Materialize(rasterOnlyTexture.TextureSource).Bytes, Materialize(mixedTexture.TextureSource).Bytes);
        Assert.Single(rasterOnlyTexture.UsedSources);
        Assert.Equal(2, mixedTexture.UsedSources.Count);
    }

    [Fact]
    public async Task EnsureTextureAsyncKeepsDefaultThirdMeshDemOverlayPixelPerfectWithinLargeBudget()
    {
        MeshCodeBounds meshBounds = MeshCodeBounds.TryParse("54372778")
            ?? throw new InvalidOperationException("Expected Matsumoto third mesh bounds.");
        TerrainTextureOverlay tileOverlay = Assert.Single(
            LocalCityGmlObjectProjection.CreateDemTerrainTextureOverlays(
                meshBounds,
                ["54372778"]));
        TerrainTextureLayoutPlan layout = TerrainTextureLayoutPlanner.Create(
            tileOverlay.GeographicBounds,
            tileOverlay.ZoomLevel);

        using TemporaryDirectory workDirectory = new();
        string rasterPath = Path.Combine(workDirectory.Path, "default-third-static-dem.png");
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
        Assert.Equal(8192, Materialize(texture.TextureSource).Width);
        Assert.Equal(4096, Materialize(texture.TextureSource).Height);
        Assert.Equal(
            new ScalarPair(
                (double)layout.CropWidth / Materialize(texture.TextureSource).Width,
                (double)layout.CropHeight / Materialize(texture.TextureSource).Height),
            texture.OccupiedUvRect.ScaleValue);
        using Image<Rgba32> outputImage = Image.LoadPixelData<Rgba32>(
            Materialize(texture.TextureSource).Bytes,
            Materialize(texture.TextureSource).Width,
            Materialize(texture.TextureSource).Height);
        int occupiedLeft = (outputImage.Width - layout.CropWidth) / 2;
        int occupiedTop = (outputImage.Height - layout.CropHeight) / 2;
        Assert.Equal(TerrainTextureAssetGenerator.DefaultDemGroundFillColor, outputImage[0, 0]);
        Assert.Equal(new Rgba32(12, 34, 56, 255), outputImage[occupiedLeft, occupiedTop]);
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
            MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
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
            Materialize(texture.TextureSource).Bytes,
            Materialize(texture.TextureSource).Width,
            Materialize(texture.TextureSource).Height);
        Assert.Equal(TerrainTextureAssetGenerator.DefaultDemGroundFillColor, outputImage[0, 0]);
        Assert.Equal(new Rgba32(12, 34, 56, 255), outputImage[1, 0]);
        Assert.Equal(TerrainTextureAssetGenerator.DefaultDemGroundFillColor, outputImage[0, 1]);
        Assert.Equal(new Rgba32(78, 90, 12, 255), outputImage[1, 1]);
    }

    [Fact]
    public async Task EnsureTextureAsyncFillsTransparentGeoReferencedRasterPixelsFromFallbackTileSource()
    {
        using TemporaryDirectory workDirectory = new();
        string rasterPath = Path.Combine(workDirectory.Path, "terrain.png");
        GeographicRectangle bounds = new(0.0, WebMercatorTileMath.MaxLatitude, -180.0, 180.0);
        TerrainTextureLayoutPlan layout = TerrainTextureLayoutPlanner.Create(bounds, 1);
        using (Image<Rgba32> rasterImage = new(layout.CropWidth, layout.CropHeight))
        {
            for (int y = 0; y < rasterImage.Height; y++)
            {
                for (int x = 0; x < rasterImage.Width; x++)
                {
                    rasterImage[x, y] = x < rasterImage.Width / 2
                        ? new Rgba32(0, 0, 0, 0)
                        : new Rgba32(12, 34, 56, 255);
                }
            }

            await rasterImage.SaveAsPngAsync(rasterPath);
        }

        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
            GeographicBounds: bounds,
            MaxTextureSize: 4096,
            Sources:
            [
                new TerrainTextureGeoReferencedRasterSource(
                    rasterPath,
                    new GeoReferencedRasterMetadata(bounds, "EPSG:4326", 1.0, 1.0)),
                new TerrainTextureTileSource("https://tiles.example/{z}/{x}/{y}.png", 1),
            ]);

        using TerrainTextureAssetGeneratorTestsProxyMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);

        GeneratedTerrainTexture texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        using Image<Rgba32> outputImage = Image.LoadPixelData<Rgba32>(
            Materialize(texture.TextureSource).Bytes,
            Materialize(texture.TextureSource).Width,
            Materialize(texture.TextureSource).Height);
        int occupiedTop = outputImage.Height - layout.CropHeight;
        Assert.NotEqual(
            TerrainTextureAssetGenerator.DefaultDemGroundFillColor,
            outputImage[layout.CropWidth / 4, occupiedTop + (layout.CropHeight / 2)]);
        Assert.Equal(
            new Rgba32(12, 34, 56, 255),
            outputImage[(layout.CropWidth * 3) / 4, occupiedTop + (layout.CropHeight / 2)]);
        Assert.NotEmpty(Materialize(texture.TextureSource).Bytes);
        Assert.Contains(texture.UsedSources, static source => source is TerrainTextureGeoReferencedRasterSource);
        Assert.Contains(
            texture.UsedSources,
            static source => source is TerrainTextureTileSource tileSource
                && tileSource.UrlTemplate == "https://tiles.example/{z}/{x}/{y}.png");
        Assert.IsType<TerrainTextureTileSource>(texture.UsedSource);
    }

    [Fact]
    public async Task EnsureTextureAsyncRejectsUnavailableGeoReferencedRasterSourceWithoutTileFallback()
    {
        GeographicRectangle bounds = new(0.0, WebMercatorTileMath.MaxLatitude, -180.0, 180.0);
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
            GeographicBounds: bounds,
            MaxTextureSize: 1024,
            Sources:
            [
                new TerrainTextureGeoReferencedRasterSource(
                    "missing.tif",
                    new GeoReferencedRasterMetadata(bounds, "EPSG:4326", 1.0, 1.0)),
                new TerrainTextureTileSource("https://tiles.example/{z}/{x}/{y}.png", 1),
            ]);

        using TerrainTextureAssetGeneratorTestsProxyMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);

        await Assert.ThrowsAsync<FileNotFoundException>(
            async () => await generator.EnsureTextureAsync(overlay, CancellationToken.None));

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task EnsureTextureAsyncRejectsNonOverlappingGeoReferencedRasterSourceWithoutTileFallback()
    {
        using TemporaryDirectory workDirectory = new();
        string rasterPath = Path.Combine(workDirectory.Path, "terrain.png");
        using (Image<Rgba32> rasterImage = new(2, 2, new Rgba32(12, 34, 56, 255)))
        {
            await rasterImage.SaveAsPngAsync(rasterPath);
        }

        GeographicRectangle requestedBounds = new(35.0, 35.001, 139.0, 139.001);
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            MeshCode: ThirdRegionalMeshCode.Parse("53394525"),
            GeographicBounds: requestedBounds,
            MaxTextureSize: 1024,
            Sources:
            [
                new TerrainTextureGeoReferencedRasterSource(
                    rasterPath,
                    new GeoReferencedRasterMetadata(
                        new GeographicRectangle(36.0, 36.001, 140.0, 140.001),
                        "EPSG:4326",
                        1.0,
                        1.0)),
                new TerrainTextureTileSource("https://tiles.example/{z}/{x}/{y}.png", 1),
            ]);

        using TerrainTextureAssetGeneratorTestsProxyMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await generator.EnsureTextureAsync(overlay, CancellationToken.None));

        Assert.Equal(0, handler.RequestCount);
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

    private static byte[] CreateClassicLittleEndianGeoTiffBytes(
        double[] modelTiePoint,
        double[] pixelScale,
        ushort[] geoKeyDirectory)
    {
        const ushort modelTiePointTag = 33922;
        const ushort pixelScaleTag = 33550;
        const ushort geoKeyDirectoryTag = 34735;
        const ushort typeShort = 3;
        const ushort typeDouble = 12;
        const int headerSize = 8;
        const int entryCount = 3;
        const int entrySize = 12;
        int ifdSize = 2 + (entryCount * entrySize) + 4;
        int pixelScaleOffset = headerSize + ifdSize;
        int tiePointOffset = pixelScaleOffset + (pixelScale.Length * sizeof(double));
        int geoKeyOffset = tiePointOffset + (modelTiePoint.Length * sizeof(double));
        byte[] bytes = new byte[geoKeyOffset + (geoKeyDirectory.Length * sizeof(ushort))];

        bytes[0] = (byte)'I';
        bytes[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2, 2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8, 2), entryCount);

        WriteClassicEntry(bytes, 10, pixelScaleTag, typeDouble, (uint)pixelScale.Length, (uint)pixelScaleOffset);
        WriteClassicEntry(bytes, 22, modelTiePointTag, typeDouble, (uint)modelTiePoint.Length, (uint)tiePointOffset);
        WriteClassicEntry(bytes, 34, geoKeyDirectoryTag, typeShort, (uint)geoKeyDirectory.Length, (uint)geoKeyOffset);

        for (int index = 0; index < pixelScale.Length; index++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(
                bytes.AsSpan(pixelScaleOffset + (index * sizeof(double)), sizeof(double)),
                unchecked((ulong)BitConverter.DoubleToInt64Bits(pixelScale[index])));
        }

        for (int index = 0; index < modelTiePoint.Length; index++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(
                bytes.AsSpan(tiePointOffset + (index * sizeof(double)), sizeof(double)),
                unchecked((ulong)BitConverter.DoubleToInt64Bits(modelTiePoint[index])));
        }

        for (int index = 0; index < geoKeyDirectory.Length; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                bytes.AsSpan(geoKeyOffset + (index * sizeof(ushort)), sizeof(ushort)),
                geoKeyDirectory[index]);
        }

        return bytes;
    }

    private static void WriteClassicEntry(byte[] bytes, int offset, ushort tag, ushort type, uint count, uint valueOrOffset)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, 2), tag);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 2, 2), type);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 4, 4), count);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 8, 4), valueOrOffset);
    }

    private static double ToMercatorY(double latitude)
    {
        double radians = latitude * (Math.PI / 180.0);
        return Math.Log(Math.Tan((Math.PI / 4.0) + (radians / 2.0)));
    }

    private static RawTexturePayload Materialize(ITextureImportSource texture)
    {
        return TextureImportSourceMaterializer.MaterializeRawAsync(texture, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }
}
