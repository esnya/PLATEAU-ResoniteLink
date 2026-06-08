using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal delegate Task<ResolvedDemTextureSources> ResolveDemTextureSources(
    PlateauImportRequest request,
    IReadOnlyList<DemTerrainOverlayRegion> overlayRegions,
    CancellationToken cancellationToken = default);

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

internal delegate Task<TerrainTextureGeoReferencedRasterSource?> ResolveDemTerrainGeoReferencedRasterSource(
    DemTerrainRasterCacheKey cacheKey,
    ThirdRegionalMeshCode meshCode,
    GeographicRectangle overlayBounds,
    CancellationToken cancellationToken);

internal readonly record struct DemTerrainGeoReferencedRasterResolver(
    DemTerrainRasterSourceScope CacheScope,
    ResolveDemTerrainGeoReferencedRasterSource ResolveRasterSourceAsync);

internal sealed class DefaultDemTextureSourcePolicy(
    Func<DatasetLocation?, CancellationToken, Task<DemTerrainGeoReferencedRasterResolver?>> createRasterResolver)
{
    public async Task<ResolvedDemTextureSources> ResolveAsync(
        PlateauImportRequest request,
        IReadOnlyList<DemTerrainOverlayRegion> overlayRegions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(overlayRegions);

        DemTerrainGeoReferencedRasterResolver? rasterResolver = await createRasterResolver(
            request.DemTextureSource,
            cancellationToken);
        if (request.DemTextureSource is not null && rasterResolver is null)
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
                rasterResolver,
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

    private static async Task<TerrainTextureOverlay> CreateOverlayAsync(
        DemTerrainOverlayRegion region,
        string datasetName,
        DemTerrainGeoReferencedRasterResolver? rasterResolver,
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

        if (rasterResolver is { } resolver)
        {
            TerrainTextureGeoReferencedRasterSource? rasterSource = await resolver.ResolveRasterSourceAsync(
                new DemTerrainRasterCacheKey(datasetName, resolver.CacheScope, region.MeshCode, region.GeographicBounds),
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
