using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal interface IDemTextureSourcePolicy
{
    Task<ResolvedDemTextureSources> ResolveAsync(
        PlateauImportRequest request,
        IReadOnlyList<string> requestedMeshCodes,
        CancellationToken cancellationToken = default);

    IReadOnlyList<TerrainTextureOverlay> CreateMapTileFallbackOverlays(
        IReadOnlyList<DemTerrainOverlayRegion> overlayRegions);
}

internal sealed record ResolvedDemTextureSources(IReadOnlyList<TerrainTextureOverlay> Overlays);

internal interface IDemTerrainGeoReferencedRasterCatalog
{
    Task<TerrainTextureGeoReferencedRasterSource?> TryResolveRasterSourceAsync(
        string cacheKey,
        string meshCode,
        GeographicRectangle overlayBounds,
        CancellationToken cancellationToken);
}

internal interface IDemTerrainGeoReferencedRasterCatalogFactory
{
    Task<IDemTerrainGeoReferencedRasterCatalog?> CreateAsync(
        PlateauImportSource? source,
        CancellationToken cancellationToken);
}

internal sealed class DefaultDemTerrainGeoReferencedRasterCatalogFactory(
    IPlateauDatasetContentSourceFactory datasetContentSourceFactory)
    : IDemTerrainGeoReferencedRasterCatalogFactory
{
    public Task<IDemTerrainGeoReferencedRasterCatalog?> CreateAsync(
        PlateauImportSource? source,
        CancellationToken cancellationToken)
    {
        return DemTerrainGeoReferencedRasterCatalog.CreateAsync(
            source,
            datasetContentSourceFactory,
            cancellationToken);
    }
}

internal sealed class LocalCityGmlDemTextureSourcePolicy(
    IDemTerrainGeoReferencedRasterCatalogFactory rasterCatalogFactory)
    : IDemTextureSourcePolicy
{
    public async Task<ResolvedDemTextureSources> ResolveAsync(
        PlateauImportRequest request,
        IReadOnlyList<string> requestedMeshCodes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(requestedMeshCodes);

        IDemTerrainGeoReferencedRasterCatalog? rasterCatalog = await rasterCatalogFactory.CreateAsync(
            request.DemTextureSource,
            cancellationToken);
        if (request.DemTextureSource is not null && rasterCatalog is null)
        {
            throw new PlateauImportValidationException(
                [LocalCityGmlImportErrorMessages.InvalidDemTextureSource(request.DemTextureSource)]);
        }

        DemTerrainOverlayRegion[] overlayRegions = LocalCityGmlDemBootstrapSupport.CreateDemTerrainOverlayRegions(
            requestedMeshCodes);
        TerrainTextureOverlay[] overlays = new TerrainTextureOverlay[overlayRegions.Length];
        for (int index = 0; index < overlayRegions.Length; index++)
        {
            overlays[index] = await CreateOverlayAsync(
                overlayRegions[index],
                rasterCatalog,
                cancellationToken);
        }

        if (request.DemTextureSource is not null
            && overlays.Any(static overlay => !overlay.EnumerateGeoReferencedRasterSources().Any()))
        {
            throw new PlateauImportValidationException(
                [LocalCityGmlImportErrorMessages.InvalidDemTextureSource(request.DemTextureSource)]);
        }

        return new ResolvedDemTextureSources(overlays);
    }

    public IReadOnlyList<TerrainTextureOverlay> CreateMapTileFallbackOverlays(
        IReadOnlyList<DemTerrainOverlayRegion> overlayRegions)
    {
        ArgumentNullException.ThrowIfNull(overlayRegions);

        return overlayRegions
            .Select(CreateFallbackOverlay)
            .ToArray();
    }

    private static async Task<TerrainTextureOverlay> CreateOverlayAsync(
        DemTerrainOverlayRegion region,
        IDemTerrainGeoReferencedRasterCatalog? rasterCatalog,
        CancellationToken cancellationToken)
    {
        List<DemTextureSourceCandidate> candidates =
        [
            CreateTileCandidate(
                DemTextureSourcePreference.Ortho19,
                region.GeographicBounds,
                new TerrainTextureTileSource(
                    LocalCityGmlObjectProjection.DefaultDemTerrainTextureUrlTemplate,
                    LocalCityGmlObjectProjection.DefaultDemTerrainTextureZoomLevel)),
            CreateTileCandidate(
                DemTextureSourcePreference.Ortho18,
                region.GeographicBounds,
                new TerrainTextureTileSource(
                    LocalCityGmlObjectProjection.DefaultDemTerrainTextureUrlTemplate,
                    LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackZoomLevel)),
            CreateTileCandidate(
                DemTextureSourcePreference.Gsi18,
                region.GeographicBounds,
                new TerrainTextureTileSource(
                    LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackUrlTemplate,
                    LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackZoomLevel)),
        ];

        TerrainTextureLicenseMode licenseMode = TerrainTextureLicenseMode.PlateauOrthoWithGsiFallback;
        if (rasterCatalog is not null)
        {
            TerrainTextureGeoReferencedRasterSource? rasterSource = await rasterCatalog.TryResolveRasterSourceAsync(
                CreateRasterCacheKey(region),
                region.Identity,
                region.GeographicBounds,
                cancellationToken);
            if (rasterSource?.Metadata is { IsUsable: true } metadata)
            {
                candidates.Add(new DemTextureSourceCandidate(
                    DemTextureSourcePreference.GeoReferencedRaster,
                    rasterSource,
                    IsExplicit: true,
                    EffectiveResolutionMeters: Math.Max(metadata.PixelWidthMeters, metadata.PixelHeightMeters)));
                licenseMode = TerrainTextureLicenseMode.PlateauOrthoOnly;
            }
        }

        return new TerrainTextureOverlay(
            PackageName: "dem",
            GeographicBounds: region.GeographicBounds,
            MaxTextureSize: LocalCityGmlObjectProjection.DefaultDemTerrainTextureMaxSize,
            Sources: OrderSources(candidates),
            LicenseMode: licenseMode);
    }

    private static TerrainTextureOverlay CreateFallbackOverlay(DemTerrainOverlayRegion region)
    {
        return new TerrainTextureOverlay(
            PackageName: "dem",
            GeographicBounds: region.GeographicBounds,
            MaxTextureSize: LocalCityGmlObjectProjection.DefaultDemTerrainTextureMaxSize,
            Sources:
            [
                new TerrainTextureTileSource(
                    LocalCityGmlObjectProjection.DefaultDemTerrainTextureUrlTemplate,
                    LocalCityGmlObjectProjection.DefaultDemTerrainTextureZoomLevel),
                new TerrainTextureTileSource(
                    LocalCityGmlObjectProjection.DefaultDemTerrainTextureUrlTemplate,
                    LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackZoomLevel),
                new TerrainTextureTileSource(
                    LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackUrlTemplate,
                    LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackZoomLevel),
            ],
            LicenseMode: TerrainTextureLicenseMode.PlateauOrthoWithGsiFallback);
    }

    private static DemTextureSourceCandidate CreateTileCandidate(
        DemTextureSourcePreference preference,
        GeographicRectangle geographicBounds,
        TerrainTextureTileSource source)
    {
        TerrainTextureLayoutPlan layoutPlan = TerrainTextureLayoutPlanner.Create(geographicBounds, source.ZoomLevel);
        double widthMeters = DegreesLongitudeToMeters(
            (geographicBounds.MinLatitude + geographicBounds.MaxLatitude) * 0.5,
            geographicBounds.MaxLongitude - geographicBounds.MinLongitude);
        double heightMeters = DegreesLatitudeToMeters(geographicBounds.MaxLatitude - geographicBounds.MinLatitude);
        double effectiveResolutionMeters = Math.Max(
            widthMeters / layoutPlan.CropWidth,
            heightMeters / layoutPlan.CropHeight);
        return new DemTextureSourceCandidate(
            preference,
            source,
            IsExplicit: false,
            EffectiveResolutionMeters: effectiveResolutionMeters);
    }

    private static TerrainTextureSource[] OrderSources(IEnumerable<DemTextureSourceCandidate> candidates)
    {
        return candidates
            .OrderByDescending(static candidate => candidate.IsExplicit)
            .ThenBy(static candidate => candidate.EffectiveResolutionMeters)
            .ThenBy(static candidate => (int)candidate.Preference)
            .Select(static candidate => candidate.Source)
            .ToArray();
    }

    private static double DegreesLatitudeToMeters(double degrees)
    {
        return Math.Abs(degrees) * 111_320.0;
    }

    private static double DegreesLongitudeToMeters(double latitude, double degrees)
    {
        return Math.Abs(degrees) * 111_320.0 * Math.Cos(latitude * (Math.PI / 180.0));
    }

    private static string CreateRasterCacheKey(DemTerrainOverlayRegion region)
    {
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{region.Identity}|{region.GeographicBounds.MinLatitude:0.######}|{region.GeographicBounds.MaxLatitude:0.######}|"
            + $"{region.GeographicBounds.MinLongitude:0.######}|{region.GeographicBounds.MaxLongitude:0.######}");
    }

    private enum DemTextureSourcePreference
    {
        Ortho19 = 0,
        GeoReferencedRaster = 1,
        Ortho18 = 2,
        Gsi18 = 3,
    }

    private sealed record DemTextureSourceCandidate(
        DemTextureSourcePreference Preference,
        TerrainTextureSource Source,
        bool IsExplicit,
        double EffectiveResolutionMeters);
}
