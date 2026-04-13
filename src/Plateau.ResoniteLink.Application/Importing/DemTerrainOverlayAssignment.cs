using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal static class DemTerrainOverlayAssignment
{
    public static IEnumerable<(LocalCityGmlResonitePlanBuilder.ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)> SplitParsedCityObject(
        LocalCityGmlResonitePlanBuilder.ParsedCityObject parsedCityObject,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays)
    {
        if (!string.Equals(parsedCityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
            || demTerrainTextureOverlays.Count == 0)
        {
            yield return (parsedCityObject, null);
            yield break;
        }

        LocalCityGmlResonitePlanBuilder.GeodeticPoint sharedOrigin = GetCityObjectOrigin(parsedCityObject);
        LocalCityGmlResonitePlanBuilder.ParsedSurface[] generatedSurfaces = parsedCityObject.Surfaces
            .Where(static surface => IsGeneratedDemTexturePath(surface.TexturePath))
            .ToArray();

        LocalCityGmlResonitePlanBuilder.ParsedSurface[] nonGeneratedSurfaces = parsedCityObject.Surfaces
            .Where(static surface => !IsGeneratedDemTexturePath(surface.TexturePath))
            .ToArray();

        if (generatedSurfaces.Length == 0)
        {
            yield return (parsedCityObject, null);
            yield break;
        }

        List<(LocalCityGmlResonitePlanBuilder.ParsedSurface Surface, TerrainTextureOverlay Overlay)> splitGeneratedSurfaces = [];
        foreach (LocalCityGmlResonitePlanBuilder.ParsedSurface generatedSurface in generatedSurfaces)
        {
            IReadOnlyList<(LocalCityGmlResonitePlanBuilder.ParsedSurface Surface, TerrainTextureOverlay Overlay)> clippedSurfaces =
                DemTerrainOverlaySurfaceClipper.ClipGeneratedSurfaceToOverlays(
                    generatedSurface,
                    demTerrainTextureOverlays);
            if (clippedSurfaces.Count > 0)
            {
                splitGeneratedSurfaces.AddRange(clippedSurfaces);
                continue;
            }

            TerrainTextureOverlay overlay = SelectOverlay(generatedSurface, demTerrainTextureOverlays);
            splitGeneratedSurfaces.Add((generatedSurface with { TexturePath = overlay.TexturePath }, overlay));
        }

        IGrouping<string, (LocalCityGmlResonitePlanBuilder.ParsedSurface Surface, TerrainTextureOverlay Overlay)>[] groups = splitGeneratedSurfaces
            .GroupBy(static surface => surface.Overlay.TexturePath, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .ToArray();

        if (groups.Length == 1 && nonGeneratedSurfaces.Length == 0)
        {
            yield return (
                parsedCityObject with
                {
                    Surfaces = groups[0].Select(static entry => entry.Surface).ToArray(),
                    OriginOverride = sharedOrigin,
                },
                groups[0].First().Overlay);
            yield break;
        }

        bool suffixGeneratedObjects = groups.Length > 1 || nonGeneratedSurfaces.Length > 0;

        for (int index = 0; index < groups.Length; index++)
        {
            IGrouping<string, (LocalCityGmlResonitePlanBuilder.ParsedSurface Surface, TerrainTextureOverlay Overlay)> group = groups[index];
            yield return (
                parsedCityObject with
                {
                    SourceIdentity = $"{parsedCityObject.SourceIdentity}_dem_{index:D2}",
                    SlotKey = $"{parsedCityObject.SlotKey}_dem_{index:D2}",
                    DisplayName = suffixGeneratedObjects
                        ? $"{parsedCityObject.DisplayName} ({index + 1})"
                        : parsedCityObject.DisplayName,
                    Surfaces = group.Select(static entry => entry.Surface).ToArray(),
                    OriginOverride = sharedOrigin,
                },
                group.First().Overlay);
        }

        if (nonGeneratedSurfaces.Length == 0)
        {
            yield break;
        }

        yield return (parsedCityObject with { Surfaces = nonGeneratedSurfaces, OriginOverride = sharedOrigin }, null);
    }

    public static TerrainTextureOverlay FindOverlay(
        string texturePath,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays)
    {
        return demTerrainTextureOverlays.First(overlay => string.Equals(overlay.TexturePath, texturePath, StringComparison.Ordinal));
    }

    public static (ResoniteFloat2? TextureScale, ResoniteFloat2? TextureOffset) TryCreateHeightMapTextureTransform(
        LocalCityGmlResonitePlanBuilder.ParsedCityObject cityObject,
        LocalCityGmlResonitePlanBuilder.MaterializedSurface materializedSurface,
        TerrainTextureOverlay? demTerrainTextureOverlay)
    {
        if (demTerrainTextureOverlay is null
            || !string.Equals(cityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
            || !IsGeneratedDemTexturePath(materializedSurface.Surface.TexturePath))
        {
            return (null, null);
        }

        GeographicRectangle overlayBounds = demTerrainTextureOverlay.GeographicBounds;
        GeographicRectangle objectBounds = IntersectGeographicBounds(
            GetCityObjectGeographicBounds(cityObject),
            overlayBounds);
        if (objectBounds.MaxLongitude <= objectBounds.MinLongitude
            || objectBounds.MaxLatitude <= objectBounds.MinLatitude)
        {
            return (null, null);
        }

        double overlayWest = WebMercatorTileMath.LongitudeToNormalizedX(overlayBounds.MinLongitude);
        double overlayEast = WebMercatorTileMath.LongitudeToNormalizedX(overlayBounds.MaxLongitude);
        double overlayNorth = WebMercatorTileMath.LatitudeToNormalizedY(overlayBounds.MaxLatitude);
        double overlaySouth = WebMercatorTileMath.LatitudeToNormalizedY(overlayBounds.MinLatitude);
        double overlayWidth = overlayEast - overlayWest;
        double overlayHeight = overlaySouth - overlayNorth;
        if (overlayWidth <= 1e-12 || overlayHeight <= 1e-12)
        {
            return (null, null);
        }

        double objectWest = WebMercatorTileMath.LongitudeToNormalizedX(objectBounds.MinLongitude);
        double objectEast = WebMercatorTileMath.LongitudeToNormalizedX(objectBounds.MaxLongitude);
        double objectNorth = WebMercatorTileMath.LatitudeToNormalizedY(objectBounds.MaxLatitude);
        double objectSouth = WebMercatorTileMath.LatitudeToNormalizedY(objectBounds.MinLatitude);

        double uMin = Math.Clamp((objectWest - overlayWest) / overlayWidth, 0.0, 1.0);
        double uMax = Math.Clamp((objectEast - overlayWest) / overlayWidth, 0.0, 1.0);
        double vMin = Math.Clamp((overlaySouth - objectSouth) / overlayHeight, 0.0, 1.0);
        double vMax = Math.Clamp((overlaySouth - objectNorth) / overlayHeight, 0.0, 1.0);

        return (
            new ResoniteFloat2(Math.Max(uMax - uMin, 1e-6), Math.Max(vMax - vMin, 1e-6)),
            new ResoniteFloat2(uMin, vMin));
    }

    private static TerrainTextureOverlay SelectOverlay(
        LocalCityGmlResonitePlanBuilder.ParsedSurface surface,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays)
    {
        GeographicRectangle surfaceBounds = GetSurfaceGeographicBounds(surface);

        TerrainTextureOverlay? containingOverlay = demTerrainTextureOverlays.FirstOrDefault(overlay =>
            surfaceBounds.MinLatitude >= overlay.GeographicBounds.MinLatitude
            && surfaceBounds.MaxLatitude <= overlay.GeographicBounds.MaxLatitude
            && surfaceBounds.MinLongitude >= overlay.GeographicBounds.MinLongitude
            && surfaceBounds.MaxLongitude <= overlay.GeographicBounds.MaxLongitude);
        if (containingOverlay is not null)
        {
            return containingOverlay;
        }

        double centerLatitude = (surfaceBounds.MinLatitude + surfaceBounds.MaxLatitude) / 2.0;
        double centerLongitude = (surfaceBounds.MinLongitude + surfaceBounds.MaxLongitude) / 2.0;
        TerrainTextureOverlay? centerOverlay = demTerrainTextureOverlays.FirstOrDefault(overlay =>
            centerLatitude >= overlay.GeographicBounds.MinLatitude
            && centerLatitude <= overlay.GeographicBounds.MaxLatitude
            && centerLongitude >= overlay.GeographicBounds.MinLongitude
            && centerLongitude <= overlay.GeographicBounds.MaxLongitude);
        if (centerOverlay is not null)
        {
            return centerOverlay;
        }

        return demTerrainTextureOverlays.First(overlay =>
            surfaceBounds.MaxLatitude >= overlay.GeographicBounds.MinLatitude
            && surfaceBounds.MinLatitude <= overlay.GeographicBounds.MaxLatitude
            && surfaceBounds.MaxLongitude >= overlay.GeographicBounds.MinLongitude
            && surfaceBounds.MinLongitude <= overlay.GeographicBounds.MaxLongitude);
    }

    private static LocalCityGmlResonitePlanBuilder.GeodeticPoint GetCityObjectOrigin(
        LocalCityGmlResonitePlanBuilder.ParsedCityObject cityObject)
    {
        return cityObject.OriginOverride
            ?? cityObject.Surfaces.SelectMany(static surface => surface.Vertices)
                .OrderBy(static point => point.Latitude)
                .ThenBy(static point => point.Longitude)
                .ThenBy(static point => point.Altitude)
                .First();
    }

    private static GeographicRectangle GetCityObjectGeographicBounds(
        LocalCityGmlResonitePlanBuilder.ParsedCityObject cityObject)
    {
        List<LocalCityGmlResonitePlanBuilder.GeodeticPoint> vertices = cityObject.Surfaces
            .SelectMany(static surface => surface.Vertices)
            .ToList();
        return new GeographicRectangle(
            MinLatitude: vertices.Min(static point => point.Latitude),
            MaxLatitude: vertices.Max(static point => point.Latitude),
            MinLongitude: vertices.Min(static point => point.Longitude),
            MaxLongitude: vertices.Max(static point => point.Longitude));
    }

    private static GeographicRectangle GetSurfaceGeographicBounds(
        LocalCityGmlResonitePlanBuilder.ParsedSurface surface)
    {
        return new GeographicRectangle(
            MinLatitude: surface.Vertices.Min(static point => point.Latitude),
            MaxLatitude: surface.Vertices.Max(static point => point.Latitude),
            MinLongitude: surface.Vertices.Min(static point => point.Longitude),
            MaxLongitude: surface.Vertices.Max(static point => point.Longitude));
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

    private static bool IsGeneratedDemTexturePath(string? texturePath)
    {
        return !string.IsNullOrWhiteSpace(texturePath)
            && texturePath.StartsWith(LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTexturePath, StringComparison.Ordinal);
    }
}
