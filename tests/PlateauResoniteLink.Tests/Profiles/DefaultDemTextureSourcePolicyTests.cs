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
    public async Task ResolveAsyncPrefersExplicitGeoReferencedRasterBeforeMapTileFallback()
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
        Assert.Same(rasterSource, overlay.Sources[0]);
        Assert.Collection(
            overlay.Sources.Skip(1),
            source => Assert.IsType<TerrainTextureTileSource>(source),
            source => Assert.IsType<TerrainTextureTileSource>(source),
            source => Assert.IsType<TerrainTextureTileSource>(source));
        Assert.Equal(TerrainTextureLicenseMode.PlateauOrthoOnly, overlay.LicenseMode);
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
    public void CreateMapTileFallbackOverlaysCreatesProviderOrderInsidePolicy()
    {
        DefaultDemTextureSourcePolicy policy = new(
            new StubDemTerrainGeoReferencedRasterCatalogFactory(
                catalog: null));

        IReadOnlyList<TerrainTextureOverlay> overlays = policy.CreateMapTileFallbackOverlays(
            [
                new DemTerrainOverlayRegion(
                    "53394525",
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
            string meshCode,
            GeographicRectangle overlayBounds,
            CancellationToken cancellationToken)
        {
            _ = cacheKey;
            _ = overlayBounds;
            rasterSourcesByMeshCode.TryGetValue(meshCode, out TerrainTextureGeoReferencedRasterSource? rasterSource);
            return Task.FromResult(rasterSource);
        }
    }

    private static IReadOnlyList<DemTerrainOverlayRegion> CreateOverlayRegions(params string[] meshCodes)
    {
        return DemSourceDiscoverySupport.CreateDemTerrainOverlayRegions(meshCodes);
    }
}
