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
        IReadOnlyList<DemTerrainOverlayRegion> overlayRegions,
        CancellationToken cancellationToken = default);

    IReadOnlyList<TerrainTextureOverlay> CreateMapTileFallbackOverlays(
        IReadOnlyList<DemTerrainOverlayRegion> overlayRegions);
}

internal sealed record ResolvedDemTextureSources(IReadOnlyList<TerrainTextureOverlay> Overlays);

internal readonly record struct DemTerrainRasterCacheKey
{
    public DemTerrainRasterCacheKey(string meshCode, GeographicRectangle overlayBounds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(meshCode);

        MeshCode = meshCode;
        MinLatitude = CanonicalizeCoordinate(overlayBounds.MinLatitude);
        MaxLatitude = CanonicalizeCoordinate(overlayBounds.MaxLatitude);
        MinLongitude = CanonicalizeCoordinate(overlayBounds.MinLongitude);
        MaxLongitude = CanonicalizeCoordinate(overlayBounds.MaxLongitude);
    }

    public string MeshCode { get; }

    public double MinLatitude { get; }

    public double MaxLatitude { get; }

    public double MinLongitude { get; }

    public double MaxLongitude { get; }

    private static double CanonicalizeCoordinate(double value)
    {
        double rounded = Math.Round(value, 6, MidpointRounding.AwayFromZero);
        return rounded == 0.0d ? 0.0d : rounded;
    }
}

internal interface IDemTerrainGeoReferencedRasterCatalog
{
    Task<TerrainTextureGeoReferencedRasterSource?> TryResolveRasterSourceAsync(
        DemTerrainRasterCacheKey cacheKey,
        string meshCode,
        GeographicRectangle overlayBounds,
        CancellationToken cancellationToken);
}

internal interface IDemTerrainGeoReferencedRasterCatalogFactory
{
    Task<IDemTerrainGeoReferencedRasterCatalog?> CreateAsync(
        DatasetLocation? source,
        CancellationToken cancellationToken);
}

internal sealed class DefaultDemTerrainGeoReferencedRasterCatalogFactory(
    IPlateauDatasetContentSourceFactory datasetContentSourceFactory)
    : IDemTerrainGeoReferencedRasterCatalogFactory
{
    public Task<IDemTerrainGeoReferencedRasterCatalog?> CreateAsync(
        DatasetLocation? source,
        CancellationToken cancellationToken)
    {
        return DemTerrainGeoReferencedRasterCatalog.CreateAsync(
            source,
            datasetContentSourceFactory,
            cancellationToken);
    }
}

internal sealed class DefaultDemTextureSourcePolicy(
    IDemTerrainGeoReferencedRasterCatalogFactory rasterCatalogFactory)
    : IDemTextureSourcePolicy
{
    public async Task<ResolvedDemTextureSources> ResolveAsync(
        PlateauImportRequest request,
        IReadOnlyList<DemTerrainOverlayRegion> overlayRegions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(overlayRegions);

        IDemTerrainGeoReferencedRasterCatalog? rasterCatalog = await rasterCatalogFactory.CreateAsync(
            request.DemTextureSource,
            cancellationToken);
        if (request.DemTextureSource is not null && rasterCatalog is null)
        {
            throw new PlateauImportValidationException(
                [LocalCityGmlImportErrorMessages.InvalidDemTextureSource(request.DemTextureSource)]);
        }

        TerrainTextureOverlay[] overlays = new TerrainTextureOverlay[overlayRegions.Count];
        for (int index = 0; index < overlayRegions.Count; index++)
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
                    DemTerrainTextureDefaults.PlateauOrthoUrlTemplate,
                    DemTerrainTextureDefaults.PlateauOrthoZoomLevel)),
            CreateTileCandidate(
                DemTextureSourcePreference.Ortho18,
                region.GeographicBounds,
                new TerrainTextureTileSource(
                    DemTerrainTextureDefaults.PlateauOrthoUrlTemplate,
                    DemTerrainTextureDefaults.FallbackZoomLevel)),
            CreateTileCandidate(
                DemTextureSourcePreference.Gsi18,
                region.GeographicBounds,
                new TerrainTextureTileSource(
                    DemTerrainTextureDefaults.GsiFallbackUrlTemplate,
                    DemTerrainTextureDefaults.FallbackZoomLevel)),
        ];

        TerrainTextureLicenseMode licenseMode = TerrainTextureLicenseMode.PlateauOrthoWithGsiFallback;
        if (rasterCatalog is not null)
        {
            TerrainTextureGeoReferencedRasterSource? rasterSource = await rasterCatalog.TryResolveRasterSourceAsync(
                new DemTerrainRasterCacheKey(region.Identity, region.GeographicBounds),
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
            MaxTextureSize: DemTerrainTextureDefaults.MaxTextureSize,
            Sources: OrderSources(candidates),
            LicenseMode: licenseMode);
    }

    private static TerrainTextureOverlay CreateFallbackOverlay(DemTerrainOverlayRegion region)
    {
        return DemTerrainTextureDefaults.CreatePlateauOrthoWithGsiFallbackOverlay(region.GeographicBounds);
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
