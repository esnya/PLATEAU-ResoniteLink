using System;
using System.Collections.Generic;
using System.IO;
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

internal readonly record struct DemTerrainRasterSourceScope
{
    public DemTerrainRasterSourceScope(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        SourcePath = Path.GetFullPath(sourcePath);
    }

    public string SourcePath { get; }
}

internal readonly record struct DemTerrainRasterCacheKey
{
    public DemTerrainRasterCacheKey(
        string datasetName,
        DemTerrainRasterSourceScope sourceScope,
        ThirdRegionalMeshCode meshCode,
        GeographicRectangle overlayBounds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetName);

        DatasetName = datasetName;
        SourceScope = sourceScope;
        MeshCode = meshCode;
        MinLatitude = CanonicalizeCoordinate(overlayBounds.MinLatitude);
        MaxLatitude = CanonicalizeCoordinate(overlayBounds.MaxLatitude);
        MinLongitude = CanonicalizeCoordinate(overlayBounds.MinLongitude);
        MaxLongitude = CanonicalizeCoordinate(overlayBounds.MaxLongitude);
    }

    public string DatasetName { get; }

    public DemTerrainRasterSourceScope SourceScope { get; }

    public ThirdRegionalMeshCode MeshCode { get; }

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
    DemTerrainRasterSourceScope CacheScope { get; }

    Task<TerrainTextureGeoReferencedRasterSource?> TryResolveRasterSourceAsync(
        DemTerrainRasterCacheKey cacheKey,
        ThirdRegionalMeshCode meshCode,
        GeographicRectangle overlayBounds,
        CancellationToken cancellationToken);
}

internal sealed class DefaultDemTextureSourcePolicy(
    Func<DatasetLocation?, CancellationToken, Task<IDemTerrainGeoReferencedRasterCatalog?>> createRasterCatalog)
    : IDemTextureSourcePolicy
{
    public async Task<ResolvedDemTextureSources> ResolveAsync(
        PlateauImportRequest request,
        IReadOnlyList<DemTerrainOverlayRegion> overlayRegions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(overlayRegions);

        IDemTerrainGeoReferencedRasterCatalog? rasterCatalog = await createRasterCatalog(
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
                request.Dataset,
                request.ExcludeGsiTerrainTiles,
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
        string datasetName,
        bool excludeGsiTerrainTiles,
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
        ];
        if (!excludeGsiTerrainTiles)
        {
            candidates.Add(
                CreateTileCandidate(
                    DemTextureSourcePreference.Gsi18,
                    region.GeographicBounds,
                    new TerrainTextureTileSource(
                        DemTerrainTextureDefaults.GsiFallbackUrlTemplate,
                        DemTerrainTextureDefaults.FallbackZoomLevel)));
        }

        if (rasterCatalog is not null)
        {
            TerrainTextureGeoReferencedRasterSource? rasterSource = await rasterCatalog.TryResolveRasterSourceAsync(
                new DemTerrainRasterCacheKey(datasetName, rasterCatalog.CacheScope, region.MeshCode, region.GeographicBounds),
                region.MeshCode,
                region.GeographicBounds,
                cancellationToken);
            if (rasterSource is not null)
            {
                candidates.Add(new DemTextureSourceCandidate(
                    DemTextureSourcePreference.GeoReferencedRaster,
                    rasterSource,
                    EffectivePixelAreaSquareMeters: rasterSource.Metadata.PixelWidthMeters * rasterSource.Metadata.PixelHeightMeters));
            }
        }

        TerrainTextureSource[] sources = OrderSources(candidates);
        return new TerrainTextureOverlay(
            PackageName: "dem",
            MeshCode: region.MeshCode,
            GeographicBounds: region.GeographicBounds,
            MaxTextureSize: DemTerrainTextureDefaults.MaxTextureSize,
            Sources: sources,
            LicenseMode: ResolveLicenseMode(sources));
    }

    private static TerrainTextureOverlay CreateFallbackOverlay(DemTerrainOverlayRegion region)
    {
        return DemTerrainTextureDefaults.CreatePlateauOrthoWithGsiFallbackOverlay(region.MeshCode, region.GeographicBounds);
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
        double pixelWidthMeters = widthMeters / layoutPlan.CropWidth;
        double pixelHeightMeters = heightMeters / layoutPlan.CropHeight;
        return new DemTextureSourceCandidate(
            preference,
            source,
            EffectivePixelAreaSquareMeters: pixelWidthMeters * pixelHeightMeters);
    }

    private static TerrainTextureSource[] OrderSources(IEnumerable<DemTextureSourceCandidate> candidates)
    {
        return candidates
            .OrderBy(static candidate => candidate.EffectivePixelAreaSquareMeters)
            .ThenBy(static candidate => (int)candidate.Preference)
            .Select(static candidate => candidate.TerrainTextureSource)
            .ToArray();
    }

    private static TerrainTextureLicenseMode ResolveLicenseMode(IEnumerable<TerrainTextureSource> sources)
    {
        return sources.Any(DemTerrainTextureDefaults.IsGsiFallbackSource)
            ? TerrainTextureLicenseMode.PlateauOrthoWithGsiFallback
            : TerrainTextureLicenseMode.PlateauOrthoOnly;
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
        GeoReferencedRaster = 0,
        Ortho19 = 1,
        Ortho18 = 2,
        Gsi18 = 3,
    }

    private sealed record DemTextureSourceCandidate(
        DemTextureSourcePreference Preference,
        TerrainTextureSource TerrainTextureSource,
        double EffectivePixelAreaSquareMeters);
}
