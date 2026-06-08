using System;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class DemTerrainOverlayUvMapper
{
    public static (Float2? TextureScale, Float2? TextureOffset) TryCreateTerrainGridTextureTransform(
        ParsedCityObject cityObject,
        ResolvedSurfaceMaterial materializedSurface,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        GeographicRectangle? cityObjectGeographicBounds = null)
    {
        TextureUvRect? occupiedUvRect = TryCreateTerrainGridOccupiedUvRect(
            cityObject,
            materializedSurface,
            demTerrainTextureOverlay,
            cityObjectGeographicBounds);
        return occupiedUvRect is null
            ? (null, null)
            : (
                new Float2(occupiedUvRect.Value.ScaleValue.X, occupiedUvRect.Value.ScaleValue.Y),
                new Float2(occupiedUvRect.Value.OffsetValue.X, occupiedUvRect.Value.OffsetValue.Y));
    }

    public static TextureUvRect? TryCreateTerrainGridOccupiedUvRect(
        ParsedCityObject cityObject,
        ResolvedSurfaceMaterial materializedSurface,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        GeographicRectangle? cityObjectGeographicBounds = null)
    {
        if (demTerrainTextureOverlay is null
            || !string.Equals(cityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
            || materializedSurface.Surface.TexturePayload is not null)
        {
            return null;
        }

        GeographicRectangle overlayBounds = demTerrainTextureOverlay.GeographicBounds;
        GeographicRectangle objectBounds = IntersectGeographicBounds(
            cityObjectGeographicBounds ?? GetCityObjectGeographicBounds(cityObject),
            overlayBounds);
        if (objectBounds.MaxLongitude <= objectBounds.MinLongitude
            || objectBounds.MaxLatitude <= objectBounds.MinLatitude)
        {
            return null;
        }

        double overlayWest = WebMercatorTileMath.LongitudeToNormalizedX(overlayBounds.MinLongitude);
        double overlayEast = WebMercatorTileMath.LongitudeToNormalizedX(overlayBounds.MaxLongitude);
        double overlayNorth = WebMercatorTileMath.LatitudeToNormalizedY(overlayBounds.MaxLatitude);
        double overlaySouth = WebMercatorTileMath.LatitudeToNormalizedY(overlayBounds.MinLatitude);
        double overlayWidth = overlayEast - overlayWest;
        double overlayHeight = overlaySouth - overlayNorth;
        if (overlayWidth <= 1e-12 || overlayHeight <= 1e-12)
        {
            return null;
        }

        double objectWest = WebMercatorTileMath.LongitudeToNormalizedX(objectBounds.MinLongitude);
        double objectEast = WebMercatorTileMath.LongitudeToNormalizedX(objectBounds.MaxLongitude);
        double objectNorth = WebMercatorTileMath.LatitudeToNormalizedY(objectBounds.MaxLatitude);
        double objectSouth = WebMercatorTileMath.LatitudeToNormalizedY(objectBounds.MinLatitude);

        double uMin = Math.Clamp((objectWest - overlayWest) / overlayWidth, 0.0, 1.0);
        double uMax = Math.Clamp((objectEast - overlayWest) / overlayWidth, 0.0, 1.0);
        double vMin = Math.Clamp((overlaySouth - objectSouth) / overlayHeight, 0.0, 1.0);
        double vMax = Math.Clamp((overlaySouth - objectNorth) / overlayHeight, 0.0, 1.0);

        return new TextureUvRect(
            uMin,
            vMin,
            Math.Max(uMax - uMin, 1e-6),
            Math.Max(vMax - vMin, 1e-6));
    }

    private static GeographicRectangle GetCityObjectGeographicBounds(
        ParsedCityObject cityObject)
    {
        return CityObjectGeographicBoundsResolver.Resolve(
            cityObject.Surfaces.SelectMany(static surface => surface.Vertices));
    }

    private static GeographicRectangle IntersectGeographicBounds(
        GeographicRectangle left,
        GeographicRectangle right)
    {
        return new GeographicRectangle(
            MinLatitude: Math.Max(left.MinLatitude, right.MinLatitude),
            MaxLatitude: Math.Min(left.MaxLatitude, right.MaxLatitude),
            MinLongitude: Math.Max(left.MinLongitude, right.MinLongitude),
            MaxLongitude: Math.Min(left.MaxLongitude, right.MaxLongitude));
    }
}
