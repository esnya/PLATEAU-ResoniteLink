using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class DefaultDemTextureSourcePolicyTests
{
    [Fact]
    public async Task ResolveAsyncOrdersSourcesBySmallestEffectivePixelArea()
    {
        GeographicRectangle rasterBounds = new(35.0, 35.01, 139.0, 139.01);
        TerrainTextureGeoReferencedRasterSource rasterSource = new(
            "C:\\ortho\\53394525.tif",
            new GeoReferencedRasterMetadata(
                rasterBounds,
                "EPSG:4326",
                PixelWidthMeters: 0.8,
                PixelHeightMeters: 0.8));
        DefaultDemTextureSourcePolicy policy = new(
            new StubDemTerrainGeoReferencedRasterCatalogFactory(
                new StubDemTerrainGeoReferencedRasterCatalog(
                    new Dictionary<string, TerrainTextureGeoReferencedRasterSource?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["53394525"] = rasterSource,
                    })));
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local("C:\\dataset"),
            DemTextureSource: DatasetLocation.Local("C:\\ortho"),
            PackageNames: ["dem"]);

        ResolvedDemTextureSources result = await policy.ResolveAsync(request, CreateOverlayRegions("53394525"));

        TerrainTextureOverlay overlay = Assert.Single(result.Overlays);
        TerrainTextureTileSource firstSource = Assert.IsType<TerrainTextureTileSource>(overlay.Sources[0]);
        Assert.Equal(LocalCityGmlObjectProjection.DefaultDemTerrainTextureUrlTemplate, firstSource.UrlTemplate);
        Assert.Equal(LocalCityGmlObjectProjection.DefaultDemTerrainTextureZoomLevel, firstSource.ZoomLevel);
        TerrainTextureTileSource secondSource = Assert.IsType<TerrainTextureTileSource>(overlay.Sources[1]);
        Assert.Equal(LocalCityGmlObjectProjection.DefaultDemTerrainTextureUrlTemplate, secondSource.UrlTemplate);
        Assert.Equal(LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackZoomLevel, secondSource.ZoomLevel);
        Assert.Contains(overlay.Sources, DemTerrainTextureDefaults.IsGsiFallbackSource);
        Assert.Same(rasterSource, overlay.Sources[^1]);
        Assert.Equal(TerrainTextureLicenseMode.PlateauOrthoWithGsiFallback, overlay.LicenseMode);
    }

    [Fact]
    public async Task ResolveAsyncPrefersExplicitGeoReferencedRasterWhenItHasSmallestPixelArea()
    {
        GeographicRectangle rasterBounds = new(35.0, 35.01, 139.0, 139.01);
        TerrainTextureGeoReferencedRasterSource rasterSource = new(
            "C:\\ortho\\53394525.tif",
            new GeoReferencedRasterMetadata(
                rasterBounds,
                "EPSG:4326",
                PixelWidthMeters: 0.01,
                PixelHeightMeters: 0.01));
        DefaultDemTextureSourcePolicy policy = new(
            new StubDemTerrainGeoReferencedRasterCatalogFactory(
                new StubDemTerrainGeoReferencedRasterCatalog(
                    new Dictionary<string, TerrainTextureGeoReferencedRasterSource?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["53394525"] = rasterSource,
                    })));
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local("C:\\dataset"),
            DemTextureSource: DatasetLocation.Local("C:\\ortho"),
            PackageNames: ["dem"]);

        ResolvedDemTextureSources result = await policy.ResolveAsync(request, CreateOverlayRegions("53394525"));

        TerrainTextureOverlay overlay = Assert.Single(result.Overlays);
        Assert.Same(rasterSource, overlay.Sources[0]);
        Assert.Collection(
            overlay.Sources.Skip(1),
            source => Assert.IsType<TerrainTextureTileSource>(source),
            source => Assert.IsType<TerrainTextureTileSource>(source),
            source => Assert.IsType<TerrainTextureTileSource>(source));
        Assert.Contains(overlay.Sources, DemTerrainTextureDefaults.IsGsiFallbackSource);
        Assert.Equal(TerrainTextureLicenseMode.PlateauOrthoWithGsiFallback, overlay.LicenseMode);
    }

    [Fact]
    public async Task ResolveAsyncUsesStableTieBreakerWhenPixelAreasMatch()
    {
        DemTerrainOverlayRegion region = Assert.Single(CreateOverlayRegions("53394525"));
        (double PixelWidthMeters, double PixelHeightMeters) ortho19PixelSize = EstimateTilePixelSizeMeters(
            region.GeographicBounds,
            LocalCityGmlObjectProjection.DefaultDemTerrainTextureZoomLevel);
        TerrainTextureGeoReferencedRasterSource rasterSource = new(
            "C:\\ortho\\53394525.tif",
            new GeoReferencedRasterMetadata(
                region.GeographicBounds,
                "EPSG:4326",
                ortho19PixelSize.PixelWidthMeters,
                ortho19PixelSize.PixelHeightMeters));
        DefaultDemTextureSourcePolicy policy = new(
            new StubDemTerrainGeoReferencedRasterCatalogFactory(
                new StubDemTerrainGeoReferencedRasterCatalog(
                    new Dictionary<string, TerrainTextureGeoReferencedRasterSource?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["53394525"] = rasterSource,
                    })));
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local("C:\\dataset"),
            DemTextureSource: DatasetLocation.Local("C:\\ortho"),
            PackageNames: ["dem"]);

        ResolvedDemTextureSources result = await policy.ResolveAsync(request, [region]);

        TerrainTextureOverlay overlay = Assert.Single(result.Overlays);
        Assert.Same(rasterSource, overlay.Sources[0]);
        TerrainTextureTileSource secondSource = Assert.IsType<TerrainTextureTileSource>(overlay.Sources[1]);
        Assert.Equal(LocalCityGmlObjectProjection.DefaultDemTerrainTextureUrlTemplate, secondSource.UrlTemplate);
        Assert.Equal(LocalCityGmlObjectProjection.DefaultDemTerrainTextureZoomLevel, secondSource.ZoomLevel);
    }

    [Fact]
    public async Task ResolveAsyncRejectsExplicitGeoTiffSourceWhenRequestedMeshIsNotCovered()
    {
        DefaultDemTextureSourcePolicy policy = new(
            new StubDemTerrainGeoReferencedRasterCatalogFactory(
                new StubDemTerrainGeoReferencedRasterCatalog(
                    new Dictionary<string, TerrainTextureGeoReferencedRasterSource?>(StringComparer.OrdinalIgnoreCase))));
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local("C:\\dataset"),
            DemTextureSource: DatasetLocation.Local("C:\\ortho"),
            PackageNames: ["dem"]);

        PlateauImportValidationException exception = await Assert.ThrowsAsync<PlateauImportValidationException>(
            () => policy.ResolveAsync(request, CreateOverlayRegions("53394525")));

        Assert.Contains(
            exception.Errors,
            static error => error.Contains("GeoTIFF", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ResolveAsyncUsesPlateauOrthoThenGsiFallbackWhenNoExplicitRasterMatches()
    {
        DefaultDemTextureSourcePolicy policy = new(
            new StubDemTerrainGeoReferencedRasterCatalogFactory(
                catalog: null));
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local("C:\\dataset"),
            PackageNames: ["dem"]);

        ResolvedDemTextureSources result = await policy.ResolveAsync(request, CreateOverlayRegions("53394525"));

        TerrainTextureOverlay overlay = Assert.Single(result.Overlays);
        Assert.Collection(
            overlay.Sources,
            source =>
            {
                TerrainTextureTileSource tile = Assert.IsType<TerrainTextureTileSource>(source);
                Assert.Equal(LocalCityGmlObjectProjection.DefaultDemTerrainTextureUrlTemplate, tile.UrlTemplate);
                Assert.Equal(LocalCityGmlObjectProjection.DefaultDemTerrainTextureZoomLevel, tile.ZoomLevel);
            },
            source =>
            {
                TerrainTextureTileSource tile = Assert.IsType<TerrainTextureTileSource>(source);
                Assert.Equal(LocalCityGmlObjectProjection.DefaultDemTerrainTextureUrlTemplate, tile.UrlTemplate);
                Assert.Equal(LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackZoomLevel, tile.ZoomLevel);
            },
            source =>
            {
                TerrainTextureTileSource tile = Assert.IsType<TerrainTextureTileSource>(source);
                Assert.Equal(LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackUrlTemplate, tile.UrlTemplate);
                Assert.Equal(LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackZoomLevel, tile.ZoomLevel);
            });
        Assert.Equal(TerrainTextureLicenseMode.PlateauOrthoWithGsiFallback, overlay.LicenseMode);
    }

    [Fact]
    public async Task ResolveAsyncExcludesGsiFallbackWhenRequested()
    {
        DefaultDemTextureSourcePolicy policy = new(
            new StubDemTerrainGeoReferencedRasterCatalogFactory(
                catalog: null));
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local("C:\\dataset"),
            PackageNames: ["dem"],
            ExcludeGsiTerrainTiles: true);

        ResolvedDemTextureSources result = await policy.ResolveAsync(request, CreateOverlayRegions("53394525"));

        TerrainTextureOverlay overlay = Assert.Single(result.Overlays);
        Assert.Collection(
            overlay.Sources,
            source =>
            {
                TerrainTextureTileSource tile = Assert.IsType<TerrainTextureTileSource>(source);
                Assert.Equal(LocalCityGmlObjectProjection.DefaultDemTerrainTextureUrlTemplate, tile.UrlTemplate);
                Assert.Equal(LocalCityGmlObjectProjection.DefaultDemTerrainTextureZoomLevel, tile.ZoomLevel);
            },
            source =>
            {
                TerrainTextureTileSource tile = Assert.IsType<TerrainTextureTileSource>(source);
                Assert.Equal(LocalCityGmlObjectProjection.DefaultDemTerrainTextureUrlTemplate, tile.UrlTemplate);
                Assert.Equal(LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackZoomLevel, tile.ZoomLevel);
            });
        Assert.DoesNotContain(overlay.Sources, DemTerrainTextureDefaults.IsGsiFallbackSource);
        Assert.Equal(TerrainTextureLicenseMode.PlateauOrthoOnly, overlay.LicenseMode);
    }

    [Fact]
    public void CreateMapTileFallbackOverlaysCreatesProviderOrderInsidePolicy()
    {
        DefaultDemTextureSourcePolicy policy = new(
            new StubDemTerrainGeoReferencedRasterCatalogFactory(
                catalog: null));

        IReadOnlyList<TerrainTextureOverlay> overlays = policy.CreateMapTileFallbackOverlays(
            [
                new DemTerrainOverlayRegion(
                    ThirdRegionalMeshCode.Parse("53394525"),
                    new GeographicRectangle(35.0, 35.01, 139.0, 139.01)),
            ]);

        TerrainTextureOverlay overlay = Assert.Single(overlays);
        Assert.Collection(
            overlay.Sources,
            source =>
            {
                TerrainTextureTileSource tile = Assert.IsType<TerrainTextureTileSource>(source);
                Assert.Equal(LocalCityGmlObjectProjection.DefaultDemTerrainTextureUrlTemplate, tile.UrlTemplate);
                Assert.Equal(LocalCityGmlObjectProjection.DefaultDemTerrainTextureZoomLevel, tile.ZoomLevel);
            },
            source =>
            {
                TerrainTextureTileSource tile = Assert.IsType<TerrainTextureTileSource>(source);
                Assert.Equal(LocalCityGmlObjectProjection.DefaultDemTerrainTextureUrlTemplate, tile.UrlTemplate);
                Assert.Equal(LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackZoomLevel, tile.ZoomLevel);
            },
            source =>
            {
                TerrainTextureTileSource tile = Assert.IsType<TerrainTextureTileSource>(source);
                Assert.Equal(LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackUrlTemplate, tile.UrlTemplate);
                Assert.Equal(LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackZoomLevel, tile.ZoomLevel);
            });
        Assert.Equal(TerrainTextureLicenseMode.PlateauOrthoWithGsiFallback, overlay.LicenseMode);
    }

    private sealed class StubDemTerrainGeoReferencedRasterCatalogFactory(IDemTerrainGeoReferencedRasterCatalog? catalog)
        : IDemTerrainGeoReferencedRasterCatalogFactory
    {
        public Task<IDemTerrainGeoReferencedRasterCatalog?> CreateAsync(
            DatasetLocation? source,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(catalog);
        }
    }

    private sealed class StubDemTerrainGeoReferencedRasterCatalog(
        IReadOnlyDictionary<string, TerrainTextureGeoReferencedRasterSource?> rasterSourcesByMeshCode)
        : IDemTerrainGeoReferencedRasterCatalog
    {
        public DemTerrainRasterSourceScope CacheScope { get; } = new("C:\\ortho");

        public Task<TerrainTextureGeoReferencedRasterSource?> TryResolveRasterSourceAsync(
            DemTerrainRasterCacheKey cacheKey,
            ThirdRegionalMeshCode meshCode,
            GeographicRectangle overlayBounds,
            CancellationToken cancellationToken)
        {
            _ = cacheKey;
            _ = overlayBounds;
            rasterSourcesByMeshCode.TryGetValue(meshCode.Value, out TerrainTextureGeoReferencedRasterSource? rasterSource);
            return Task.FromResult(rasterSource);
        }
    }

    private static IReadOnlyList<DemTerrainOverlayRegion> CreateOverlayRegions(params string[] meshCodes)
    {
        return DemSourceDiscoverySupport.CreateDemTerrainOverlayRegions(meshCodes);
    }

    private static (double PixelWidthMeters, double PixelHeightMeters) EstimateTilePixelSizeMeters(
        GeographicRectangle geographicBounds,
        int zoomLevel)
    {
        TerrainTextureLayoutPlan layoutPlan = TerrainTextureLayoutPlanner.Create(geographicBounds, zoomLevel);
        double widthMeters = Math.Abs(geographicBounds.MaxLongitude - geographicBounds.MinLongitude)
            * 111_320.0
            * Math.Cos(((geographicBounds.MinLatitude + geographicBounds.MaxLatitude) * 0.5) * (Math.PI / 180.0));
        double heightMeters = Math.Abs(geographicBounds.MaxLatitude - geographicBounds.MinLatitude) * 111_320.0;
        return (widthMeters / layoutPlan.CropWidth, heightMeters / layoutPlan.CropHeight);
    }
}
