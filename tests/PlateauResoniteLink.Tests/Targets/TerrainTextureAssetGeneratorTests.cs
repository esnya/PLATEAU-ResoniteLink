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
    public void TexturePowerOfTwoRoundUpRejectsValuesAboveRepresentablePositivePower()
    {
        Assert.Equal(1 << 30, TexturePowerOfTwo.RoundUp((1 << 30) - 1));
        Assert.Equal(1 << 30, TexturePowerOfTwo.RoundUp(1 << 30));

        Assert.Throws<ArgumentOutOfRangeException>(
            static () => TexturePowerOfTwo.RoundUp((1 << 30) + 1));
    }

    [Fact]
    public void TerrainTextureTileSourceRejectsZoomLevelsThatOverflowIntTileIndexes()
    {
        TerrainTextureTileSource source = new("https://tiles.example/{z}/{x}/{y}.png", WebMercatorTileMath.MaxZoomLevel);

        Assert.Equal(WebMercatorTileMath.MaxZoomLevel, source.ZoomLevel);
        Assert.Throws<ArgumentOutOfRangeException>(
            static () => new TerrainTextureTileSource("https://tiles.example/{z}/{x}/{y}.png", WebMercatorTileMath.MaxZoomLevel + 1));
    }

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
        Assert.Equal(
            new ScalarPair(
                (double)layout.CropWidth / RoundUpToPowerOfTwo(layout.CropWidth),
                (double)layout.CropHeight / RoundUpToPowerOfTwo(layout.CropHeight)),
            firstTexture.OccupiedUvRect.ScaleValue);

        using Image<Rgba32> image = LoadImage(firstTexture.TextureSource);
        Assert.Equal(RoundUpToPowerOfTwo(layout.CropWidth), image.Width);
        Assert.NotEmpty(Materialize(firstTexture.TextureSource).Bytes);
    }

    [Fact]
    public async Task EnsureTextureAsyncPacksIntoPowerOfTwoCanvasWithoutResizing()
    {
        using FakeMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);

        TerrainTextureOverlay overlay = CreateFullCoverageOverlay("https://tiles.example/{z}/{x}/{y}.png", maxTextureSize: 256);

        GeneratedTerrainTexture texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        using Image<Rgba32> image = LoadImage(texture.TextureSource);
        Assert.Equal(256, image.Width);
        Assert.Equal(256, image.Height);
        Assert.InRange(texture.OccupiedUvRect.ScaleValue.X, 0.0, 1.0);
        Assert.InRange(texture.OccupiedUvRect.ScaleValue.Y, 0.0, 1.0);
        Assert.NotEmpty(Materialize(texture.TextureSource).Bytes);
    }

    [Fact]
    public async Task EnsureTextureAsyncUsesPowerOfTwoCanvasForCropWithinBudget()
    {
        using FakeMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);
        ThirdRegionalMeshCode meshCode = ThirdRegionalMeshCode.Parse("53394525");
        GeographicRectangle bounds = ToGeographicRectangle(meshCode.Bounds);
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            MeshCode: meshCode,
            UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
            ZoomLevel: 1,
            GeographicBounds: bounds,
            MaxTextureSize: 512);
        TerrainTextureLayoutPlan layout = TerrainTextureLayoutPlanner.Create(overlay.GeographicBounds, overlay.ZoomLevel);

        GeneratedTerrainTexture texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        using Image<Rgba32> image = LoadImage(texture.TextureSource);
        Assert.Equal(RoundUpToPowerOfTwo(layout.CropWidth), Materialize(texture.TextureSource).Width);
        Assert.Equal(RoundUpToPowerOfTwo(layout.CropHeight), Materialize(texture.TextureSource).Height);
        Assert.Equal(
            new ScalarPair(
                (double)layout.CropWidth / RoundUpToPowerOfTwo(layout.CropWidth),
                (double)layout.CropHeight / RoundUpToPowerOfTwo(layout.CropHeight)),
            texture.OccupiedUvRect.ScaleValue);
        Assert.NotEmpty(Materialize(texture.TextureSource).Bytes);
    }

    [Fact]
    public async Task EnsureTextureAsyncFallsBackToPowerOfTwoResizeWhenPowerOfTwoCanvasWouldExceedBudget()
    {
        using FakeMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);

        TerrainTextureOverlay overlay = CreateFullCoverageOverlay("https://tiles.example/{z}/{x}/{y}.png", maxTextureSize: 300);

        GeneratedTerrainTexture texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        using Image<Rgba32> image = LoadImage(texture.TextureSource);
        Assert.Equal(256, image.Width);
        Assert.Equal(256, image.Height);
        Assert.InRange(texture.OccupiedUvRect.ScaleValue.X, 0.0, 1.0);
        Assert.InRange(texture.OccupiedUvRect.ScaleValue.Y, 0.0, 1.0);
    }

    [Fact]
    public async Task EnsureTextureAsyncKeepsNorthUpOrientationAcrossStitchedTiles()
    {
        using FakeMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);

        ThirdRegionalMeshCode meshCode = ThirdRegionalMeshCode.Parse("53394525");
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            MeshCode: meshCode,
            UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
            ZoomLevel: 1,
            GeographicBounds: ToGeographicRectangle(meshCode.Bounds),
            MaxTextureSize: 4096);
        TerrainTextureLayoutPlan layout = TerrainTextureLayoutPlanner.Create(overlay.GeographicBounds, overlay.ZoomLevel);

        GeneratedTerrainTexture texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        using Image<Rgba32> image = LoadImage(texture.TextureSource);
        Assert.Equal(RoundUpToPowerOfTwo(layout.CropWidth), image.Width);
        Rectangle occupied = ToTopLeftPixelRect(texture.OccupiedUvRect, image.Width, image.Height);
        Assert.NotEmpty(Materialize(texture.TextureSource).Bytes);
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
        Assert.True(handler.RequestCount > 0);
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
            Assert.True(firstHandler.RequestCount > 0);
        }

        using FakeMapTileHandler secondHandler = new();
        using HttpClient secondClient = new(secondHandler);
        TerrainTextureAssetGenerator secondGenerator = new(secondClient, cacheRoot.Path);

        GeneratedTerrainTexture texture = await secondGenerator.EnsureTextureAsync(overlay, CancellationToken.None);

        Assert.Equal(0, secondHandler.RequestCount);
        using FakeMapTileHandler thirdHandler = new();
        using HttpClient thirdClient = new(thirdHandler);
        TerrainTextureAssetGenerator thirdGenerator = new(thirdClient, cacheRoot.Path);
        GeneratedTerrainTexture repeatedTexture = await thirdGenerator.EnsureTextureAsync(overlay, CancellationToken.None);

        Assert.Equal(Materialize(texture.TextureSource).Width, Materialize(repeatedTexture.TextureSource).Width);
        Assert.Equal(Materialize(texture.TextureSource).Height, Materialize(repeatedTexture.TextureSource).Height);
        Assert.Equal(Materialize(texture.TextureSource).Bytes, Materialize(repeatedTexture.TextureSource).Bytes);
    }

    [Fact]
    public async Task EnsureTextureAsyncCreatesSeparateTextureWhenTileSourceChanges()
    {
        using FakeMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);
        TerrainTextureOverlay firstOverlay = CreateFullCoverageOverlay("https://tiles-a.example/{z}/{x}/{y}.png");
        TerrainTextureOverlay secondOverlay = CreateFullCoverageOverlay("https://tiles-b.example/{z}/{x}/{y}.png");

        GeneratedTerrainTexture firstTexture = await generator.EnsureTextureAsync(firstOverlay, CancellationToken.None);
        GeneratedTerrainTexture secondTexture = await generator.EnsureTextureAsync(secondOverlay, CancellationToken.None);

        Assert.NotSame(firstTexture, secondTexture);
    }

    [Fact]
    public async Task EnsureTextureAsyncCreatesSeparateTextureWhenMeshCodeChanges()
    {
        using FakeMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);
        TerrainTextureOverlay firstOverlay = CreateFullCoverageOverlay("https://tiles.example/{z}/{x}/{y}.png");
        TerrainTextureOverlay secondOverlay = CreateFullCoverageOverlay(
            "https://tiles.example/{z}/{x}/{y}.png",
            meshCode: "53394526");

        GeneratedTerrainTexture firstTexture = await generator.EnsureTextureAsync(firstOverlay, CancellationToken.None);
        GeneratedTerrainTexture secondTexture = await generator.EnsureTextureAsync(secondOverlay, CancellationToken.None);

        Assert.NotSame(firstTexture, secondTexture);
    }

    [Fact]
    public void EnsureGeographicBoundsMatchMeshCodeRejectsBoundsOutsideMeshTolerance()
    {
        ThirdRegionalMeshCode meshCode = ThirdRegionalMeshCode.Parse("53394525");
        GeographicRectangle bounds = ToGeographicRectangle(meshCode.Bounds);
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            MeshCode: meshCode,
            UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
            ZoomLevel: 1,
            GeographicBounds: bounds with { MinLatitude = bounds.MinLatitude + 0.00001 },
            MaxTextureSize: 4096);

        Assert.Throws<ArgumentException>(overlay.EnsureGeographicBoundsMatchMeshCode);
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
            Assert.True(firstHandler.RequestCount > 0);
        }

        using FakeMapTileHandler secondHandler = new();
        using HttpClient secondClient = new(secondHandler);
        TerrainTextureAssetGenerator secondGenerator = new(secondClient, cacheRoot.Path, disablePersistentCache: true);

        _ = await secondGenerator.EnsureTextureAsync(overlay, CancellationToken.None);

        Assert.True(secondHandler.RequestCount > 0);
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

        Assert.Equal(RoundUpToPowerOfTwo(TerrainTextureLayoutPlanner.Create(overlay).CropWidth), Materialize(texture.TextureSource).Width);
        Assert.True(handler.RequestCount > 0);
    }

    [Fact]
    public async Task EnsureTextureAsyncCachesPartiallyCoveredTileGeneration()
    {
        using FlakyMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);
        TerrainTextureOverlay overlay = CreateFullCoverageOverlay("https://tiles.example/{z}/{x}/{y}.png");

        GeneratedTerrainTexture firstTexture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);
        GeneratedTerrainTexture texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        Assert.Same(firstTexture, texture);
        Assert.Equal(RoundUpToPowerOfTwo(TerrainTextureLayoutPlanner.Create(overlay).CropWidth), Materialize(texture.TextureSource).Width);
        Assert.True(handler.RequestCount > 0);
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
            _ = await firstGenerator.EnsureTextureAsync(overlay, CancellationToken.None);
        }

        using RetryableMapTileHandler secondHandler = new();
        using HttpClient secondClient = new(secondHandler);
        TerrainTextureAssetGenerator secondGenerator = new(secondClient, cacheRoot.Path);

        GeneratedTerrainTexture texture = await secondGenerator.EnsureTextureAsync(overlay, CancellationToken.None);

        Assert.Equal(RoundUpToPowerOfTwo(TerrainTextureLayoutPlanner.Create(overlay).CropWidth), Materialize(texture.TextureSource).Width);
        Assert.Equal(1, secondHandler.RequestCount);
    }

    [Fact]
    public async Task EnsureTextureAsyncRefetchesWhenPersistentCacheEntryIsCorrupt()
    {
        using TemporaryDirectory cacheRoot = new();
        TerrainTextureOverlay overlay = CreateFullCoverageOverlay("https://tiles.example/{z}/{x}/{y}.png");
        PersistentTerrainTileCache persistentCache = new(cacheRoot.Path);
        using MemoryStream corruptTileContent = new([1, 2, 3, 4]);
        await persistentCache.WriteTileAsync(
            overlay.UrlTemplate,
            overlay.ZoomLevel,
            0,
            0,
            corruptTileContent,
            CancellationToken.None);

        using FakeMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, cacheRoot.Path);

        GeneratedTerrainTexture texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        Assert.Equal(RoundUpToPowerOfTwo(TerrainTextureLayoutPlanner.Create(overlay).CropWidth), Materialize(texture.TextureSource).Width);
        Assert.True(handler.RequestCount > 0);
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
        GeneratedTerrainTexture texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        using Image<Rgba32> image = LoadImage(texture.TextureSource);
        Rectangle occupied = ToTopLeftPixelRect(texture.OccupiedUvRect, image.Width, image.Height);
        Assert.True(ContainsColor(image, 255, 0, 0));
        Assert.True(ContainsColor(image, 0, 255, 0));
    }

    [Fact]
    public async Task EnsureTextureAsyncUsesSecondaryPrimaryTilesBeforeGsiFallback()
    {
        using SecondaryPrimaryFallbackMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);
        ThirdRegionalMeshCode meshCode = ThirdRegionalMeshCode.Parse("53394525");
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            MeshCode: meshCode,
            GeographicBounds: ToGeographicRectangle(meshCode.Bounds),
            MaxTextureSize: 4096,
            Sources:
            [
                new TerrainTextureTileSource("https://primary.example/{z}/{x}/{y}.png", 2),
                new TerrainTextureTileSource("https://primary.example/{z}/{x}/{y}.png", 1),
                new TerrainTextureTileSource("https://fallback.example/{z}/{x}/{y}.png", 1),
            ]);

        GeneratedTerrainTexture texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        Assert.NotEmpty(Materialize(texture.TextureSource).Bytes);
        Assert.IsType<TerrainTextureTileSource>(texture.UsedSource);
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
        const int primaryZoomLevel = 18;
        ThirdRegionalMeshCode meshCode = ThirdRegionalMeshCode.Parse("53394525");
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            MeshCode: meshCode,
            GeographicBounds: ToGeographicRectangle(meshCode.Bounds),
            MaxTextureSize: 4096,
            PrimarySource: new TerrainTextureTileSource("https://primary.example/{z}/{x}/{y}.png", primaryZoomLevel),
            FallbackSource: new TerrainTextureTileSource("https://fallback.example/{z}/{x}/{y}.png", 17));

        _ = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        string[] requestedSources = handler.Requests
            .Select(static request => $"{request.Host}|{request.ZoomLevel}")
            .ToArray();
        Assert.Contains($"primary.example|{primaryZoomLevel}", requestedSources);
        Assert.Contains("fallback.example|17", requestedSources);
    }

    [Fact]
    public async Task EnsureTextureAsyncPartiallyFallsBackFromPrimaryToSecondarySource()
    {
        ThirdRegionalMeshCode meshCode = ThirdRegionalMeshCode.Parse("53394525");
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            MeshCode: meshCode,
            GeographicBounds: ToGeographicRectangle(meshCode.Bounds),
            MaxTextureSize: 4096,
            PrimarySource: new TerrainTextureTileSource("https://primary.example/{z}/{x}/{y}.png", 18),
            FallbackSource: new TerrainTextureTileSource("https://fallback.example/{z}/{x}/{y}.png", 18));
        TerrainTextureLayoutPlan layout = TerrainTextureLayoutPlanner.Create(overlay);
        using PartialSourceFallbackMapTileHandler handler = new((layout.MaxTileX, layout.MaxTileY));
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);

        GeneratedTerrainTexture texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        Assert.NotEmpty(Materialize(texture.TextureSource).Bytes);
        Assert.Contains(
            texture.UsedSources ?? [],
            static source => source is TerrainTextureTileSource tileSource
                && tileSource.UrlTemplate == "https://primary.example/{z}/{x}/{y}.png");
        Assert.Contains("primary.example", handler.RequestedHosts);
        Assert.Contains("fallback.example", handler.RequestedHosts);

        using Image<Rgba32> image = LoadImage(texture.TextureSource);
        Assert.True(ContainsColor(image, 255, 0, 0));
        Assert.True(ContainsColor(image, 255, 0, 255));
    }

    [Fact]
    public async Task EnsureTextureAsyncFillsRemainingTileGapsWithDefaultGroundColor()
    {
        ThirdRegionalMeshCode meshCode = ThirdRegionalMeshCode.Parse("53394525");
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            MeshCode: meshCode,
            GeographicBounds: ToGeographicRectangle(meshCode.Bounds),
            MaxTextureSize: 4096,
            PrimarySource: new TerrainTextureTileSource("https://primary.example/{z}/{x}/{y}.png", 18));
        TerrainTextureLayoutPlan layout = TerrainTextureLayoutPlanner.Create(overlay);
        using MissingTileMapTileHandler handler = new((layout.MaxTileX, layout.MaxTileY));
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);

        GeneratedTerrainTexture texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        using Image<Rgba32> image = LoadImage(texture.TextureSource);
        Assert.True(ContainsColor(image, 255, 0, 0));
        Rgba32 fillColor = TerrainTextureAssetGenerator.DefaultDemGroundFillColor;
        Assert.True(ContainsColor(image, fillColor.R, fillColor.G, fillColor.B));
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

        ThirdRegionalMeshCode meshCode = ThirdRegionalMeshCode.Parse("53394525");
        GeographicRectangle requestedBounds = ToGeographicRectangle(meshCode.Bounds);
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            MeshCode: meshCode,
            GeographicBounds: requestedBounds,
            MaxTextureSize: 16,
            Sources:
            [
                new TerrainTextureGeoReferencedRasterSource(
                    rasterPath,
                    new GeoReferencedRasterMetadata(
                        requestedBounds with
                        {
                            MaxLongitude = (requestedBounds.MinLongitude + requestedBounds.MaxLongitude) / 2.0,
                        },
                        "EPSG:4326",
                        1.0,
                        1.0)),
                new TerrainTextureTileSource("https://tiles.example/{z}/{x}/{y}.png", 18),
            ]);

        using FakeMapTileHandler handler = new();
        using HttpClient httpClient = new(handler);
        TerrainTextureAssetGenerator generator = new(httpClient, disablePersistentCache: true);

        GeneratedTerrainTexture texture = await generator.EnsureTextureAsync(overlay, CancellationToken.None);

        using Image<Rgba32> outputImage = LoadImage(texture.TextureSource);
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

        Assert.Equal(RoundUpToPowerOfTwo(TerrainTextureLayoutPlanner.Create(overlay).CropWidth), Materialize(texture.TextureSource).Width);
        Assert.True(handler.RequestCount > 1);
    }

    private static TerrainTextureOverlay CreateFullCoverageOverlay(
        string urlTemplate,
        string? fallbackUrlTemplate = null,
        int maxTextureSize = 4096,
        string meshCode = "53394525",
        int zoomLevel = 18)
    {
        ThirdRegionalMeshCode thirdMeshCode = ThirdRegionalMeshCode.Parse(meshCode);
        return new TerrainTextureOverlay(
            PackageName: "dem",
            MeshCode: thirdMeshCode,
            UrlTemplate: urlTemplate,
            ZoomLevel: zoomLevel,
            GeographicBounds: ToGeographicRectangle(thirdMeshCode.Bounds),
            MaxTextureSize: maxTextureSize,
            FallbackUrlTemplate: fallbackUrlTemplate);
    }

    private static GeographicRectangle ToGeographicRectangle(JisRegionalMeshBounds bounds)
    {
        return new GeographicRectangle(
            bounds.SouthLatitude,
            bounds.NorthLatitude,
            bounds.WestLongitude,
            bounds.EastLongitude);
    }

    private static int ExpectedTileRequestCount(TerrainTextureOverlay overlay)
    {
        TerrainTextureLayoutPlan layout = TerrainTextureLayoutPlanner.Create(overlay);
        return (layout.MaxTileX - layout.MinTileX + 1) * (layout.MaxTileY - layout.MinTileY + 1);
    }

    private static Image<Rgba32> LoadImage(ITextureImportSource texture)
    {
        RawTexturePayload rawPayload = Materialize(texture);
        return Image.LoadPixelData<Rgba32>(rawPayload.Bytes, rawPayload.Width, rawPayload.Height);
    }

    private static RawTexturePayload Materialize(ITextureImportSource texture)
    {
        return TextureImportSourceMaterializer.MaterializeRawAsync(texture, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
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

    private static Rectangle ToTopLeftPixelRect(TextureUvRect uvRect, int imageWidth, int imageHeight)
    {
        int x = (int)Math.Round(uvRect.MinU * imageWidth, MidpointRounding.AwayFromZero);
        int y = (int)Math.Round((1.0 - uvRect.MaxV) * imageHeight, MidpointRounding.AwayFromZero);
        int width = (int)Math.Round(uvRect.Width * imageWidth, MidpointRounding.AwayFromZero);
        int height = (int)Math.Round(uvRect.Height * imageHeight, MidpointRounding.AwayFromZero);
        int clampedX = Math.Clamp(x, 0, imageWidth - 1);
        int clampedY = Math.Clamp(y, 0, imageHeight - 1);

        return new Rectangle(
            clampedX,
            clampedY,
            Math.Clamp(width, 1, imageWidth - clampedX),
            Math.Clamp(height, 1, imageHeight - clampedY));
    }

    private static Rgba32 Sample(Rectangle rect, Image<Rgba32> image, double xFraction, double yFractionFromTop)
    {
        int x = rect.X + Math.Clamp((int)Math.Floor(rect.Width * xFraction), 0, rect.Width - 1);
        int y = rect.Y + Math.Clamp((int)Math.Floor(rect.Height * yFractionFromTop), 0, rect.Height - 1);
        return image[x, y];
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
            return (tileX & 1, tileY & 1) switch
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

    private sealed class MissingTileMapTileHandler((int x, int y) missingTile) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] segments = request.RequestUri!.AbsolutePath.Trim('/').Split('/');
            int tileX = int.Parse(segments[^2], CultureInfo.InvariantCulture);
            int tileY = int.Parse(Path.GetFileNameWithoutExtension(segments[^1]), CultureInfo.InvariantCulture);

            if ((tileX, tileY) == missingTile)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            using Image<Rgba32> image = new(
                WebMercatorTileMath.TileSizePixels,
                WebMercatorTileMath.TileSizePixels,
                FakeMapTileHandler.GetTileColorForTests(tileX, tileY));
            MemoryStream stream = new();
            await image.SaveAsPngAsync(stream, cancellationToken);
            stream.Position = 0;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(stream),
            };
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
