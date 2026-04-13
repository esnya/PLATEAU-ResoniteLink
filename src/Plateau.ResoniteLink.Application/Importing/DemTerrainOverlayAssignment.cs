using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal static class DemTerrainOverlayAssignment
{
    private const double BoundarySliverMaxThicknessMeters = 0.10;
    private const double BoundarySliverMaxAreaRatio = 0.01;

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
                if (TryCollapseBoundarySliverSplit(generatedSurface, clippedSurfaces, out (LocalCityGmlResonitePlanBuilder.ParsedSurface Surface, TerrainTextureOverlay Overlay) collapsedSurface))
                {
                    splitGeneratedSurfaces.Add(collapsedSurface);
                    continue;
                }

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
        ArgumentException.ThrowIfNullOrWhiteSpace(texturePath);
        ArgumentNullException.ThrowIfNull(demTerrainTextureOverlays);

        TerrainTextureOverlay? matchingOverlay = demTerrainTextureOverlays.FirstOrDefault(
            overlay => string.Equals(overlay.TexturePath, texturePath, StringComparison.Ordinal));
        if (matchingOverlay is not null)
        {
            return matchingOverlay;
        }

        throw new InvalidOperationException(
            string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"Failed to resolve DEM terrain overlay for texture path '{texturePath}'. "
                + $"OverlayCount={demTerrainTextureOverlays.Count}. "
                + $"KnownOverlays=[{DescribeOverlaySet(demTerrainTextureOverlays)}]"));
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

        TerrainTextureOverlay? intersectingOverlay = demTerrainTextureOverlays.FirstOrDefault(overlay =>
            surfaceBounds.MaxLatitude >= overlay.GeographicBounds.MinLatitude
            && surfaceBounds.MinLatitude <= overlay.GeographicBounds.MaxLatitude
            && surfaceBounds.MaxLongitude >= overlay.GeographicBounds.MinLongitude
            && surfaceBounds.MinLongitude <= overlay.GeographicBounds.MaxLongitude);
        if (intersectingOverlay is not null)
        {
            return intersectingOverlay;
        }

        return FindNearestOverlay(surfaceBounds, demTerrainTextureOverlays);
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

    private static TerrainTextureOverlay FindNearestOverlay(
        GeographicRectangle surfaceBounds,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays)
    {
        if (demTerrainTextureOverlays.Count == 0)
        {
            throw new InvalidOperationException(
                string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"Failed to resolve any DEM terrain overlay for surface bounds "
                    + $"lat[{surfaceBounds.MinLatitude:F9}, {surfaceBounds.MaxLatitude:F9}] "
                    + $"lon[{surfaceBounds.MinLongitude:F9}, {surfaceBounds.MaxLongitude:F9}] because no overlays were available."));
        }

        double surfaceCenterLatitude = (surfaceBounds.MinLatitude + surfaceBounds.MaxLatitude) * 0.5;
        double surfaceCenterLongitude = (surfaceBounds.MinLongitude + surfaceBounds.MaxLongitude) * 0.5;
        return demTerrainTextureOverlays
            .OrderBy(overlay => GetOverlayDistanceSquared(surfaceCenterLatitude, surfaceCenterLongitude, overlay.GeographicBounds))
            .ThenBy(static overlay => overlay.TexturePath, StringComparer.Ordinal)
            .First();
    }

    private static double GetOverlayDistanceSquared(
        double latitude,
        double longitude,
        GeographicRectangle overlayBounds)
    {
        double clampedLatitude = Math.Clamp(latitude, overlayBounds.MinLatitude, overlayBounds.MaxLatitude);
        double clampedLongitude = Math.Clamp(longitude, overlayBounds.MinLongitude, overlayBounds.MaxLongitude);
        double deltaLatitude = latitude - clampedLatitude;
        double deltaLongitude = longitude - clampedLongitude;
        return (deltaLatitude * deltaLatitude) + (deltaLongitude * deltaLongitude);
    }

    private static string DescribeOverlaySet(IReadOnlyList<TerrainTextureOverlay> overlays)
    {
        return string.Join(
            ", ",
            overlays.Select(static overlay =>
                string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"{overlay.TexturePath}:lat[{overlay.GeographicBounds.MinLatitude:F8},{overlay.GeographicBounds.MaxLatitude:F8}]"
                    + $" lon[{overlay.GeographicBounds.MinLongitude:F8},{overlay.GeographicBounds.MaxLongitude:F8}]")));
    }

    private static bool TryCollapseBoundarySliverSplit(
        LocalCityGmlResonitePlanBuilder.ParsedSurface sourceSurface,
        IReadOnlyList<(LocalCityGmlResonitePlanBuilder.ParsedSurface Surface, TerrainTextureOverlay Overlay)> clippedSurfaces,
        out (LocalCityGmlResonitePlanBuilder.ParsedSurface Surface, TerrainTextureOverlay Overlay) collapsedSurface)
    {
        collapsedSurface = default;
        if (clippedSurfaces.Count <= 1)
        {
            return false;
        }

        SurfaceMetrics[] metrics = clippedSurfaces
            .Select(static entry => ComputeSurfaceMetrics(entry.Surface))
            .ToArray();
        double totalArea = metrics.Sum(static metric => metric.AreaSquareMeters);
        if (totalArea <= 1e-9)
        {
            return false;
        }

        int dominantIndex = 0;
        for (int index = 1; index < metrics.Length; index++)
        {
            if (metrics[index].AreaSquareMeters > metrics[dominantIndex].AreaSquareMeters)
            {
                dominantIndex = index;
            }
        }

        bool hasBoundarySliver = false;
        for (int index = 0; index < metrics.Length; index++)
        {
            if (index == dominantIndex)
            {
                continue;
            }

            double areaRatio = metrics[index].AreaSquareMeters / totalArea;
            if (areaRatio > BoundarySliverMaxAreaRatio
                || metrics[index].EstimatedThicknessMeters > BoundarySliverMaxThicknessMeters)
            {
                return false;
            }

            hasBoundarySliver = true;
        }

        if (!hasBoundarySliver)
        {
            return false;
        }

        TerrainTextureOverlay dominantOverlay = clippedSurfaces[dominantIndex].Overlay;
        collapsedSurface = (sourceSurface with { TexturePath = dominantOverlay.TexturePath }, dominantOverlay);
        return true;
    }

    private static SurfaceMetrics ComputeSurfaceMetrics(LocalCityGmlResonitePlanBuilder.ParsedSurface surface)
    {
        LocalCityGmlResonitePlanBuilder.GeodeticPoint[] vertices = surface.ExteriorRing.Vertices;
        if (vertices.Length < 3)
        {
            return new SurfaceMetrics(0.0, 0.0);
        }

        double referenceLatitude = vertices.Average(static point => point.Latitude) * (Math.PI / 180.0);
        double referenceLongitude = vertices.Average(static point => point.Longitude);
        double metersPerLatitudeDegree = 111_320.0;
        double metersPerLongitudeDegree = metersPerLatitudeDegree * Math.Cos(referenceLatitude);

        ProjectedPoint[] projected = vertices
            .Select(point => new ProjectedPoint(
                (point.Longitude - referenceLongitude) * metersPerLongitudeDegree,
                (point.Latitude - vertices[0].Latitude) * metersPerLatitudeDegree))
            .ToArray();

        double signedArea = 0.0;
        double maxDistance = 0.0;
        for (int index = 0; index < projected.Length; index++)
        {
            ProjectedPoint current = projected[index];
            ProjectedPoint next = projected[(index + 1) % projected.Length];
            signedArea += (current.X * next.Y) - (next.X * current.Y);
            maxDistance = Math.Max(maxDistance, Distance(current, next));
        }

        double areaSquareMeters = Math.Abs(signedArea) * 0.5;
        double estimatedThicknessMeters = maxDistance <= 1e-9
            ? 0.0
            : (2.0 * areaSquareMeters) / maxDistance;
        return new SurfaceMetrics(areaSquareMeters, estimatedThicknessMeters);
    }

    private static double Distance(ProjectedPoint left, ProjectedPoint right)
    {
        double deltaX = right.X - left.X;
        double deltaY = right.Y - left.Y;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }

    private readonly record struct ProjectedPoint(double X, double Y);

    private readonly record struct SurfaceMetrics(double AreaSquareMeters, double EstimatedThicknessMeters);
}
