using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class DemTerrainOverlayAssignment
{
    private const double BoundarySliverMaxThicknessMeters = 0.10;
    private const double BoundarySliverMaxAreaRatio = 0.01;
    private const double BoundarySliverMaxAreaSquareMeters = 4.0;

    public static bool HasOverlayCoverage(
        BootstrapParsedCityObject parsedCityObject,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds>? requestedMeshAreas = null)
    {
        ArgumentNullException.ThrowIfNull(parsedCityObject);
        if (!string.Equals(parsedCityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        BootstrapParsedSurface[] generatedSurfaces = parsedCityObject.Surfaces
            .Where(static surface => surface.UsesGeneratedDemTexture)
            .ToArray();
        if (generatedSurfaces.Length == 0 || demTerrainTextureOverlays.Count == 0)
        {
            return true;
        }

        GeographicRectangle[] requestedMeshBounds = requestedMeshAreas is null
            ? []
            : requestedMeshAreas
                .Select(static area => new GeographicRectangle(
                    area.SouthLatitude,
                    area.NorthLatitude,
                    area.WestLongitude,
                    area.EastLongitude))
                .Distinct()
                .ToArray();

        foreach (BootstrapParsedSurface generatedSurface in generatedSurfaces)
        {
            BootstrapParsedSurface[] requestedMeshClippedSurfaces =
                parsedCityObject.SharedAcrossMeshCodes
                    ? ClipBootstrapGeneratedSurfaceToRequestedMeshAreas(generatedSurface, requestedMeshBounds)
                    : [generatedSurface];
            if (requestedMeshClippedSurfaces.Length == 0)
            {
                continue;
            }

            foreach (BootstrapParsedSurface requestedMeshClippedSurface in requestedMeshClippedSurfaces)
            {
                if (!requestedMeshClippedSurface.Vertices.Any())
                {
                    return false;
                }

                GeographicRectangle surfaceBounds = GetSurfaceGeographicBounds(requestedMeshClippedSurface);
                bool hasContainingOverlay = demTerrainTextureOverlays.Any(overlay =>
                    surfaceBounds.MinLatitude >= overlay.GeographicBounds.MinLatitude
                    && surfaceBounds.MaxLatitude <= overlay.GeographicBounds.MaxLatitude
                    && surfaceBounds.MinLongitude >= overlay.GeographicBounds.MinLongitude
                    && surfaceBounds.MaxLongitude <= overlay.GeographicBounds.MaxLongitude);
                if (hasContainingOverlay)
                {
                    continue;
                }

                TerrainTextureOverlay[] candidateOverlays = demTerrainTextureOverlays
                    .Where(overlay =>
                        surfaceBounds.MaxLatitude >= overlay.GeographicBounds.MinLatitude
                        && surfaceBounds.MinLatitude <= overlay.GeographicBounds.MaxLatitude
                        && surfaceBounds.MaxLongitude >= overlay.GeographicBounds.MinLongitude
                        && surfaceBounds.MinLongitude <= overlay.GeographicBounds.MaxLongitude)
                    .ToArray();
                if (candidateOverlays.Length == 0)
                {
                    return false;
                }

                IReadOnlyList<(BootstrapParsedSurface Surface, TerrainTextureOverlay Overlay)> clippedSurfaces =
                    DemTerrainOverlaySurfaceClipper.ClipGeneratedSurfaceToOverlays(
                        requestedMeshClippedSurface,
                        candidateOverlays);
                if (clippedSurfaces.Count == 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static IEnumerable<(BootstrapParsedCityObject CityObject, TerrainTextureOverlay? Overlay)> SplitParsedCityObject(
        BootstrapParsedCityObject parsedCityObject,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds>? requestedMeshAreas = null)
    {
        if (!string.Equals(parsedCityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
        {
            yield return (parsedCityObject, null);
            yield break;
        }

        GeodeticPoint sharedOrigin = GetCityObjectOrigin(parsedCityObject);
        GeographicRectangle[] requestedMeshBounds = requestedMeshAreas is null
            ? []
            : requestedMeshAreas
                .Select(static area => new GeographicRectangle(
                    area.SouthLatitude,
                    area.NorthLatitude,
                    area.WestLongitude,
                    area.EastLongitude))
                .Distinct()
                .ToArray();

        BootstrapParsedSurface[] generatedSurfaces = parsedCityObject.Surfaces
            .Where(static surface => surface.UsesGeneratedDemTexture)
            .ToArray();

        BootstrapParsedSurface[] nonGeneratedSurfaces = parsedCityObject.Surfaces
            .Where(static surface => !surface.UsesGeneratedDemTexture)
            .SelectMany(surface => parsedCityObject.SharedAcrossMeshCodes
                ? ClipSurfaceToRequestedMeshAreas(surface, requestedMeshBounds)
                : [surface])
            .ToArray();

        if (generatedSurfaces.Length == 0)
        {
            if (nonGeneratedSurfaces.Length == 0)
            {
                yield break;
            }

            yield return (parsedCityObject with { Surfaces = nonGeneratedSurfaces, OriginOverride = sharedOrigin }, null);
            yield break;
        }

        if (demTerrainTextureOverlays.Count == 0)
        {
            BootstrapParsedSurface[] texturelessGeneratedSurfaces = generatedSurfaces
                .SelectMany(generatedSurface => parsedCityObject.SharedAcrossMeshCodes
                    ? ClipGeneratedSurfaceToRequestedMeshAreas(generatedSurface, requestedMeshBounds)
                    : [generatedSurface])
                .ToArray();
            BootstrapParsedSurface[] texturelessSurfaces = [.. texturelessGeneratedSurfaces, .. nonGeneratedSurfaces];
            if (texturelessSurfaces.Length == 0)
            {
                yield break;
            }

            yield return (parsedCityObject with { Surfaces = texturelessSurfaces, OriginOverride = sharedOrigin }, null);
            yield break;
        }

        List<(BootstrapParsedSurface Surface, TerrainTextureOverlay Overlay)> splitGeneratedSurfaces = [];
        foreach (BootstrapParsedSurface generatedSurface in generatedSurfaces)
        {
            BootstrapParsedSurface[] requestedMeshClippedSurfaces =
                parsedCityObject.SharedAcrossMeshCodes
                    ? ClipGeneratedSurfaceToRequestedMeshAreas(generatedSurface, requestedMeshBounds)
                    : [generatedSurface];
            if (requestedMeshClippedSurfaces.Length == 0)
            {
                continue;
            }

            foreach (BootstrapParsedSurface requestedMeshClippedSurface in requestedMeshClippedSurfaces)
            {
                GeographicRectangle surfaceBounds = GetSurfaceGeographicBounds(requestedMeshClippedSurface);
                TerrainTextureOverlay? containingOverlay = demTerrainTextureOverlays.FirstOrDefault(overlay =>
                    surfaceBounds.MinLatitude >= overlay.GeographicBounds.MinLatitude
                    && surfaceBounds.MaxLatitude <= overlay.GeographicBounds.MaxLatitude
                    && surfaceBounds.MinLongitude >= overlay.GeographicBounds.MinLongitude
                    && surfaceBounds.MaxLongitude <= overlay.GeographicBounds.MaxLongitude);
                if (containingOverlay is not null)
                {
                    splitGeneratedSurfaces.Add((requestedMeshClippedSurface, containingOverlay));
                    continue;
                }

                TerrainTextureOverlay[] candidateOverlays = demTerrainTextureOverlays
                    .Where(overlay =>
                        surfaceBounds.MaxLatitude >= overlay.GeographicBounds.MinLatitude
                        && surfaceBounds.MinLatitude <= overlay.GeographicBounds.MaxLatitude
                        && surfaceBounds.MaxLongitude >= overlay.GeographicBounds.MinLongitude
                        && surfaceBounds.MinLongitude <= overlay.GeographicBounds.MaxLongitude)
                    .ToArray();
                if (candidateOverlays.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"Requested-mesh-clipped DEM surface '{requestedMeshClippedSurface.PolygonId}' has no matching terrain overlay coverage.");
                }

                IReadOnlyList<(BootstrapParsedSurface Surface, TerrainTextureOverlay Overlay)> clippedSurfaces =
                    DemTerrainOverlaySurfaceClipper.ClipGeneratedSurfaceToOverlays(
                        requestedMeshClippedSurface,
                        candidateOverlays);
                if (clippedSurfaces.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Requested-mesh-clipped DEM surface '{requestedMeshClippedSurface.PolygonId}' did not produce any terrain-overlay-clipped geometry.");
                }

                if (TryPruneBoundarySliverSplit(
                        clippedSurfaces,
                        out IReadOnlyList<(BootstrapParsedSurface Surface, TerrainTextureOverlay Overlay)> prunedSurfaces))
                {
                    splitGeneratedSurfaces.AddRange(prunedSurfaces);
                }
                else
                {
                    splitGeneratedSurfaces.AddRange(clippedSurfaces);
                }
            }
        }

        IGrouping<TerrainTextureOverlay, (BootstrapParsedSurface Surface, TerrainTextureOverlay Overlay)>[] groups = splitGeneratedSurfaces
            .GroupBy(static surface => surface.Overlay)
            .OrderBy(static group => group.Key.PackageName, StringComparer.Ordinal)
            .ThenBy(static group => group.Key.GeographicBounds.MinLatitude)
            .ThenBy(static group => group.Key.GeographicBounds.MinLongitude)
            .ToArray();

        if (groups.Length == 1
            && nonGeneratedSurfaces.Length == 0
            && splitGeneratedSurfaces.Count > 0)
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
            IGrouping<TerrainTextureOverlay, (BootstrapParsedSurface Surface, TerrainTextureOverlay Overlay)> group = groups[index];
            yield return (
                parsedCityObject with
                {
                    SlotKey = $"{parsedCityObject.SlotKey}_dem_{index:D2}",
                    DisplayName = suffixGeneratedObjects
                        ? $"{parsedCityObject.DisplayName} ({index + 1})"
                        : parsedCityObject.DisplayName,
                    Surfaces = group.Select(static entry => entry.Surface).ToArray(),
                    OriginOverride = sharedOrigin,
                },
                group.First().Overlay);
        }

        BootstrapParsedSurface[] untexturedSurfaces = nonGeneratedSurfaces;
        if (untexturedSurfaces.Length == 0)
        {
            yield break;
        }

        yield return (parsedCityObject with { Surfaces = untexturedSurfaces, OriginOverride = sharedOrigin }, null);
    }

    private static BootstrapParsedSurface[] ClipGeneratedSurfaceToRequestedMeshAreas(
        BootstrapParsedSurface generatedSurface,
        GeographicRectangle[] requestedMeshBounds)
    {
        if (requestedMeshBounds.Length == 0)
        {
            return [generatedSurface];
        }

        return DemTerrainOverlaySurfaceClipper.ClipGeneratedSurfaceToBounds(
            generatedSurface,
            requestedMeshBounds).ToArray();
    }

    private static BootstrapParsedSurface[] ClipSurfaceToRequestedMeshAreas(
        BootstrapParsedSurface surface,
        GeographicRectangle[] requestedMeshBounds)
    {
        if (requestedMeshBounds.Length == 0)
        {
            return [surface];
        }

        return DemTerrainOverlaySurfaceClipper.ClipSurfaceToBounds(
            surface,
            requestedMeshBounds).ToArray();
    }

    private static BootstrapParsedSurface[] ClipBootstrapGeneratedSurfaceToRequestedMeshAreas(
        BootstrapParsedSurface generatedSurface,
        GeographicRectangle[] requestedMeshBounds)
    {
        if (requestedMeshBounds.Length == 0)
        {
            return [generatedSurface];
        }

        return DemTerrainOverlaySurfaceClipper.ClipGeneratedSurfaceToBounds(
            generatedSurface,
            requestedMeshBounds).ToArray();
    }

    public static (Float2? TextureScale, Float2? TextureOffset) TryCreateHeightMapTextureTransform(
        BootstrapParsedCityObject cityObject,
        LocalCityGmlObjectProjection.ResolvedSurfaceMaterial materializedSurface,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        GeographicRectangle? cityObjectGeographicBounds = null)
    {
        TextureUvRect? occupiedUvRect = TryCreateHeightMapOccupiedUvRect(
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

    public static TextureUvRect? TryCreateHeightMapOccupiedUvRect(
        BootstrapParsedCityObject cityObject,
        LocalCityGmlObjectProjection.ResolvedSurfaceMaterial materializedSurface,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        GeographicRectangle? cityObjectGeographicBounds = null)
    {
        if (demTerrainTextureOverlay is null
            || !string.Equals(cityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
            || !materializedSurface.Surface.UsesGeneratedDemTexture)
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

    private static bool TryPruneBoundarySliverSplit(
        IReadOnlyList<(BootstrapParsedSurface Surface, TerrainTextureOverlay Overlay)> clippedSurfaces,
        out IReadOnlyList<(BootstrapParsedSurface Surface, TerrainTextureOverlay Overlay)> prunedSurfaces)
    {
        prunedSurfaces = [];
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

        List<(BootstrapParsedSurface Surface, TerrainTextureOverlay Overlay)> keptSurfaces =
        [
            clippedSurfaces[dominantIndex],
        ];
        bool prunedBoundarySliver = false;
        for (int index = 0; index < metrics.Length; index++)
        {
            if (index == dominantIndex)
            {
                continue;
            }

            double areaRatio = metrics[index].AreaSquareMeters / totalArea;
            bool isBoundarySliver =
                areaRatio <= BoundarySliverMaxAreaRatio
                || metrics[index].AreaSquareMeters <= BoundarySliverMaxAreaSquareMeters
                || metrics[index].EstimatedThicknessMeters <= BoundarySliverMaxThicknessMeters;
            if (!isBoundarySliver)
            {
                keptSurfaces.Add(clippedSurfaces[index]);
                continue;
            }

            prunedBoundarySliver = true;
        }

        if (!prunedBoundarySliver)
        {
            return false;
        }

        if (keptSurfaces.Count == 1)
        {
            prunedSurfaces = [keptSurfaces[0]];
            return true;
        }

        prunedSurfaces = keptSurfaces
            .OrderBy(static entry => entry.Overlay.PackageName, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Overlay.GeographicBounds.MinLatitude)
            .ThenBy(static entry => entry.Overlay.GeographicBounds.MinLongitude)
            .ToArray();
        return true;
    }

    private static GeodeticPoint GetCityObjectOrigin(
        BootstrapParsedCityObject cityObject)
    {
        if (cityObject.OriginOverride is not null)
        {
            return cityObject.OriginOverride!;
        }

        bool hasPoint = false;
        GeodeticPoint? origin = null;
        foreach (BootstrapParsedSurface surface in cityObject.Surfaces)
        {
            foreach (GeodeticPoint point in surface.Vertices)
            {
                if (!hasPoint
                    || point.Latitude < origin!.Latitude
                    || (point.Latitude.Equals(origin.Latitude) && point.Longitude < origin.Longitude)
                    || (point.Latitude.Equals(origin.Latitude)
                        && point.Longitude.Equals(origin.Longitude)
                        && point.Altitude < origin.Altitude))
                {
                    origin = point;
                    hasPoint = true;
                }
            }
        }

        if (!hasPoint)
        {
            throw new InvalidOperationException("DEM city object has no vertices.");
        }

        return origin!;
    }

    private static GeographicRectangle GetCityObjectGeographicBounds(
        BootstrapParsedCityObject cityObject)
    {
        bool hasPoint = false;
        double minLatitude = 0.0;
        double maxLatitude = 0.0;
        double minLongitude = 0.0;
        double maxLongitude = 0.0;
        foreach (BootstrapParsedSurface surface in cityObject.Surfaces)
        {
            foreach (GeodeticPoint point in surface.Vertices)
            {
                if (!hasPoint)
                {
                    minLatitude = maxLatitude = point.Latitude;
                    minLongitude = maxLongitude = point.Longitude;
                    hasPoint = true;
                    continue;
                }

                minLatitude = Math.Min(minLatitude, point.Latitude);
                maxLatitude = Math.Max(maxLatitude, point.Latitude);
                minLongitude = Math.Min(minLongitude, point.Longitude);
                maxLongitude = Math.Max(maxLongitude, point.Longitude);
            }
        }

        if (!hasPoint)
        {
            throw new InvalidOperationException("DEM city object has no vertices.");
        }

        return new GeographicRectangle(
            MinLatitude: minLatitude,
            MaxLatitude: maxLatitude,
            MinLongitude: minLongitude,
            MaxLongitude: maxLongitude);
    }

    private static GeographicRectangle GetSurfaceGeographicBounds(
        BootstrapParsedSurface surface)
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

    private static SurfaceMetrics ComputeSurfaceMetrics(BootstrapParsedSurface surface)
    {
        GeodeticPoint[] vertices = surface.ExteriorRing.Vertices;
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
