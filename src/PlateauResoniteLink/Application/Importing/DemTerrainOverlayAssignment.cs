using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class DemTerrainOverlayAssignment
{
    private const double BoundarySliverMaxThicknessMeters = 0.10;
    private const double BoundarySliverMaxAreaRatio = 0.01;
    private const double BoundarySliverMaxAreaSquareMeters = 4.0;

    public static bool HasOverlayCoverage(
        ParsedCityObject parsedCityObject,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds)
    {
        ArgumentNullException.ThrowIfNull(parsedCityObject);
        if (!string.Equals(parsedCityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        ParsedSurface[] generatedSurfaces = parsedCityObject.Surfaces
            .Where(static surface => surface.UsesGeneratedDemTexture)
            .ToArray();
        if (generatedSurfaces.Length == 0 || demTerrainTextureOverlays.Count == 0)
        {
            return true;
        }

        GeographicRectangle[] requestedMeshBounds = CreateRequestedMeshBounds(requestedMeshCodeBounds);

        foreach (ParsedSurface generatedSurface in generatedSurfaces)
        {
            ParsedSurface[] requestedMeshClippedSurfaces =
                ClipGeneratedSurfaceToRequestedMeshCodeBounds(generatedSurface, requestedMeshBounds);
            if (requestedMeshClippedSurfaces.Length == 0)
            {
                continue;
            }

            foreach (ParsedSurface requestedMeshClippedSurface in requestedMeshClippedSurfaces)
            {
                if (!requestedMeshClippedSurface.Vertices.Any())
                {
                    return false;
                }

                GeographicRectangle surfaceBounds = GetSurfaceGeographicBounds(requestedMeshClippedSurface);
                TerrainOverlayCoverage coverage = ResolveTerrainOverlayCoverage(
                    surfaceBounds,
                    demTerrainTextureOverlays);
                if (coverage.Kind == TerrainOverlayCoverageKind.Contained)
                {
                    continue;
                }

                if (coverage.Kind == TerrainOverlayCoverageKind.None)
                {
                    return false;
                }

                IReadOnlyList<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> clippedSurfaces =
                    DemTerrainOverlaySurfaceClipper.ClipGeneratedSurfaceToOverlays(
                        requestedMeshClippedSurface,
                        coverage.IntersectingOverlays);
                if (clippedSurfaces.Count == 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static bool HasOverlayCoverage(
        ParsedCityObject parsedCityObject,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays)
    {
        return HasOverlayCoverage(parsedCityObject, demTerrainTextureOverlays, []);
    }

    public static IEnumerable<(ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)> SplitParsedCityObject(
        ParsedCityObject parsedCityObject,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
        bool allowMissingGeneratedDemOverlayCoverage = false,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(parsedCityObject.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
        {
            yield return (parsedCityObject, null);
            yield break;
        }

        GeodeticPoint sharedOrigin = GetCityObjectOrigin(parsedCityObject);
        GeographicRectangle[] requestedMeshBounds = CreateRequestedMeshBounds(requestedMeshCodeBounds);

        ParsedSurface[] generatedSurfaces = parsedCityObject.Surfaces
            .Where(static surface => surface.UsesGeneratedDemTexture)
            .ToArray();

        progressReporter?.Invoke(
            PlateauLog.Debug(
                "import",
                $"Splitting DEM city object '{parsedCityObject.SlotKey}' "
                + $"(generated_surfaces={generatedSurfaces.Length}, non_generated_surfaces={parsedCityObject.Surfaces.Length - generatedSurfaces.Length}, "
                + $"overlays={demTerrainTextureOverlays.Count}, requested_mesh_code_bounds={requestedMeshBounds.Length})."));

        ParsedSurface[] nonGeneratedSurfaces = parsedCityObject.Surfaces
            .Where(static surface => !surface.UsesGeneratedDemTexture)
            .SelectMany(surface => parsedCityObject.SharedAcrossMeshCodes
                ? ClipSurfaceToRequestedMeshCodeBounds(surface, requestedMeshBounds, progressReporter, cancellationToken)
                : [surface])
            .ToArray();

        if (generatedSurfaces.Length == 0)
        {
            if (nonGeneratedSurfaces.Length == 0)
            {
                yield break;
            }

            yield return (parsedCityObject with { Surfaces = nonGeneratedSurfaces, GeodeticOriginOverride = sharedOrigin }, null);
            yield break;
        }

        if (demTerrainTextureOverlays.Count == 0)
        {
            ParsedSurface[] allTexturelessGeneratedSurfaces = generatedSurfaces
                .SelectMany(generatedSurface => ClipGeneratedSurfaceToRequestedMeshCodeBounds(
                    generatedSurface,
                    requestedMeshBounds,
                    progressReporter,
                    cancellationToken))
                .ToArray();
            ParsedSurface[] texturelessSurfaces = [.. allTexturelessGeneratedSurfaces, .. nonGeneratedSurfaces];
            if (texturelessSurfaces.Length == 0)
            {
                yield break;
            }

            yield return (parsedCityObject with { Surfaces = texturelessSurfaces, GeodeticOriginOverride = sharedOrigin }, null);
            yield break;
        }

        List<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> splitGeneratedSurfaces = [];
        List<ParsedSurface> texturelessGeneratedSurfaces = [];
        foreach (ParsedSurface generatedSurface in generatedSurfaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ParsedSurface[] requestedMeshClippedSurfaces =
                ClipGeneratedSurfaceToRequestedMeshCodeBounds(
                    generatedSurface,
                    requestedMeshBounds,
                    progressReporter,
                    cancellationToken);
            if (requestedMeshClippedSurfaces.Length == 0)
            {
                continue;
            }

            foreach (ParsedSurface requestedMeshClippedSurface in requestedMeshClippedSurfaces)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GeographicRectangle surfaceBounds = GetSurfaceGeographicBounds(requestedMeshClippedSurface);
                TerrainOverlayCoverage coverage = ResolveTerrainOverlayCoverage(
                    surfaceBounds,
                    demTerrainTextureOverlays);
                if (coverage.Kind == TerrainOverlayCoverageKind.Contained)
                {
                    splitGeneratedSurfaces.Add((requestedMeshClippedSurface, coverage.ContainingOverlay!));
                    continue;
                }

                if (coverage.Kind == TerrainOverlayCoverageKind.None)
                {
                    if (allowMissingGeneratedDemOverlayCoverage)
                    {
                        texturelessGeneratedSurfaces.Add(requestedMeshClippedSurface);
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"Mesh-code-bounds-clipped DEM surface '{requestedMeshClippedSurface.PolygonId}' has no matching terrain overlay coverage.");
                }

                IReadOnlyList<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> clippedSurfaces =
                    DemTerrainOverlaySurfaceClipper.ClipGeneratedSurfaceToOverlays(
                        requestedMeshClippedSurface,
                        coverage.IntersectingOverlays,
                        progressReporter,
                        cancellationToken);
                if (allowMissingGeneratedDemOverlayCoverage)
                {
                    texturelessGeneratedSurfaces.AddRange(
                        ClipGeneratedSurfaceOutsideOverlays(
                            requestedMeshClippedSurface,
                            coverage.IntersectingOverlays,
                            progressReporter,
                            cancellationToken));
                }

                if (clippedSurfaces.Count == 0)
                {
                    if (allowMissingGeneratedDemOverlayCoverage)
                    {
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"Mesh-code-bounds-clipped DEM surface '{requestedMeshClippedSurface.PolygonId}' did not produce any terrain-overlay-clipped geometry.");
                }

                splitGeneratedSurfaces.AddRange(clippedSurfaces);
            }
        }

        IReadOnlyList<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> groupedGeneratedSurfaces =
            TryPruneBoundarySliverGroups(
                splitGeneratedSurfaces,
                out IReadOnlyList<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> prunedGeneratedSurfaces)
                ? prunedGeneratedSurfaces
                : splitGeneratedSurfaces;

        IGrouping<TerrainTextureOverlay, (ParsedSurface Surface, TerrainTextureOverlay Overlay)>[] groups = groupedGeneratedSurfaces
            .GroupBy(static surface => surface.Overlay)
            .OrderBy(static group => group.Key.PackageName, StringComparer.Ordinal)
            .ThenBy(static group => group.Key.GeographicBounds.MinLatitude)
            .ThenBy(static group => group.Key.GeographicBounds.MinLongitude)
            .ToArray();

        if (groups.Length == 1
            && nonGeneratedSurfaces.Length == 0
            && texturelessGeneratedSurfaces.Count == 0
            && groupedGeneratedSurfaces.Count > 0)
        {
            yield return (
                parsedCityObject with
                {
                    Surfaces = groups[0].Select(static entry => entry.Surface).ToArray(),
                    GeodeticOriginOverride = sharedOrigin,
                },
                groups[0].First().Overlay);
            yield break;
        }

        bool suffixGeneratedObjects = groups.Length > 1 || nonGeneratedSurfaces.Length > 0 || texturelessGeneratedSurfaces.Count > 0;

        for (int index = 0; index < groups.Length; index++)
        {
            IGrouping<TerrainTextureOverlay, (ParsedSurface Surface, TerrainTextureOverlay Overlay)> group = groups[index];
            cancellationToken.ThrowIfCancellationRequested();
            yield return (
                parsedCityObject with
                {
                    SlotKey = $"{parsedCityObject.SlotKey}_dem_{index:D2}",
                    DisplayName = suffixGeneratedObjects
                        ? $"{parsedCityObject.DisplayName} ({index + 1})"
                        : parsedCityObject.DisplayName,
                    Surfaces = group.Select(static entry => entry.Surface).ToArray(),
                    GeodeticOriginOverride = sharedOrigin,
                },
                group.First().Overlay);
        }

        ParsedSurface[] untexturedSurfaces = [.. texturelessGeneratedSurfaces, .. nonGeneratedSurfaces];
        if (untexturedSurfaces.Length == 0)
        {
            yield break;
        }

        yield return (parsedCityObject with { Surfaces = untexturedSurfaces, GeodeticOriginOverride = sharedOrigin }, null);
    }

    public static IEnumerable<(ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)> SplitParsedCityObject(
        ParsedCityObject parsedCityObject,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays)
    {
        return SplitParsedCityObject(parsedCityObject, demTerrainTextureOverlays, []);
    }

    private static GeographicRectangle[] CreateRequestedMeshBounds(
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds)
    {
        return requestedMeshCodeBounds
            .Select(static area => new GeographicRectangle(
                area.SouthLatitude,
                area.NorthLatitude,
                area.WestLongitude,
                area.EastLongitude))
            .Distinct()
            .ToArray();
    }

    private static ParsedSurface[] ClipGeneratedSurfaceToRequestedMeshCodeBounds(
        ParsedSurface generatedSurface,
        GeographicRectangle[] requestedMeshBounds,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        if (requestedMeshBounds.Length == 0)
        {
            return [generatedSurface];
        }

        return DemTerrainOverlaySurfaceClipper.ClipGeneratedSurfaceToBounds(
            generatedSurface,
            requestedMeshBounds,
            progressReporter,
            cancellationToken).ToArray();
    }

    private static ParsedSurface[] ClipSurfaceToRequestedMeshCodeBounds(
        ParsedSurface surface,
        GeographicRectangle[] requestedMeshBounds,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        if (requestedMeshBounds.Length == 0)
        {
            return [surface];
        }

        return DemTerrainOverlaySurfaceClipper.ClipSurfaceToBounds(
            surface,
            requestedMeshBounds,
            progressReporter,
            cancellationToken).ToArray();
    }

    private static ParsedSurface[] ClipGeneratedSurfaceToRequestedMeshCodeBounds(
        ParsedSurface generatedSurface,
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

    private static IReadOnlyList<ParsedSurface> ClipGeneratedSurfaceOutsideOverlays(
        ParsedSurface generatedSurface,
        IReadOnlyList<TerrainTextureOverlay> overlays,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        GeographicRectangle surfaceBounds = GetSurfaceGeographicBounds(generatedSurface);
        GeographicRectangle[] uncoveredBounds = CreateUncoveredOverlayCellBounds(surfaceBounds, overlays);
        if (uncoveredBounds.Length == 0)
        {
            return [];
        }

        return DemTerrainOverlaySurfaceClipper.ClipGeneratedSurfaceToBounds(
            generatedSurface,
            uncoveredBounds,
            progressReporter,
            cancellationToken);
    }

    private static GeographicRectangle[] CreateUncoveredOverlayCellBounds(
        GeographicRectangle surfaceBounds,
        IReadOnlyList<TerrainTextureOverlay> overlays)
    {
        SortedSet<double> latitudes = [surfaceBounds.MinLatitude, surfaceBounds.MaxLatitude];
        SortedSet<double> longitudes = [surfaceBounds.MinLongitude, surfaceBounds.MaxLongitude];
        foreach (TerrainTextureOverlay overlay in overlays)
        {
            GeographicRectangle bounds = overlay.GeographicBounds;
            if (!Intersects(surfaceBounds, bounds))
            {
                continue;
            }

            latitudes.Add(Math.Clamp(bounds.MinLatitude, surfaceBounds.MinLatitude, surfaceBounds.MaxLatitude));
            latitudes.Add(Math.Clamp(bounds.MaxLatitude, surfaceBounds.MinLatitude, surfaceBounds.MaxLatitude));
            longitudes.Add(Math.Clamp(bounds.MinLongitude, surfaceBounds.MinLongitude, surfaceBounds.MaxLongitude));
            longitudes.Add(Math.Clamp(bounds.MaxLongitude, surfaceBounds.MinLongitude, surfaceBounds.MaxLongitude));
        }

        double[] latitudeEdges = latitudes.ToArray();
        double[] longitudeEdges = longitudes.ToArray();
        List<GeographicRectangle> uncoveredBounds = [];
        for (int latitudeIndex = 0; latitudeIndex < latitudeEdges.Length - 1; latitudeIndex++)
        {
            double minLatitude = latitudeEdges[latitudeIndex];
            double maxLatitude = latitudeEdges[latitudeIndex + 1];
            if (maxLatitude - minLatitude <= 0.0)
            {
                continue;
            }

            double centerLatitude = (minLatitude + maxLatitude) / 2.0;
            for (int longitudeIndex = 0; longitudeIndex < longitudeEdges.Length - 1; longitudeIndex++)
            {
                double minLongitude = longitudeEdges[longitudeIndex];
                double maxLongitude = longitudeEdges[longitudeIndex + 1];
                if (maxLongitude - minLongitude <= 0.0)
                {
                    continue;
                }

                double centerLongitude = (minLongitude + maxLongitude) / 2.0;
                bool covered = overlays.Any(overlay => ContainsPoint(overlay.GeographicBounds, centerLatitude, centerLongitude));
                if (!covered)
                {
                    uncoveredBounds.Add(new GeographicRectangle(minLatitude, maxLatitude, minLongitude, maxLongitude));
                }
            }
        }

        return uncoveredBounds.ToArray();
    }

    private static TerrainOverlayCoverage ResolveTerrainOverlayCoverage(
        GeographicRectangle surfaceBounds,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays)
    {
        TerrainTextureOverlay? containingOverlay = demTerrainTextureOverlays.FirstOrDefault(overlay =>
            Contains(overlay.GeographicBounds, surfaceBounds));
        if (containingOverlay is not null)
        {
            return TerrainOverlayCoverage.Contained(containingOverlay);
        }

        TerrainTextureOverlay[] intersectingOverlays = demTerrainTextureOverlays
            .Where(overlay => Intersects(overlay.GeographicBounds, surfaceBounds))
            .ToArray();
        return intersectingOverlays.Length == 0
            ? TerrainOverlayCoverage.None
            : TerrainOverlayCoverage.Intersecting(intersectingOverlays);
    }

    private static bool ContainsPoint(
        GeographicRectangle rectangle,
        double latitude,
        double longitude)
    {
        return latitude >= rectangle.MinLatitude
            && latitude <= rectangle.MaxLatitude
            && longitude >= rectangle.MinLongitude
            && longitude <= rectangle.MaxLongitude;
    }

    private static bool Contains(
        GeographicRectangle container,
        GeographicRectangle subject)
    {
        return subject.MinLatitude >= container.MinLatitude
            && subject.MaxLatitude <= container.MaxLatitude
            && subject.MinLongitude >= container.MinLongitude
            && subject.MaxLongitude <= container.MaxLongitude;
    }

    private static bool Intersects(
        GeographicRectangle left,
        GeographicRectangle right)
    {
        return right.MaxLatitude >= left.MinLatitude
            && right.MinLatitude <= left.MaxLatitude
            && right.MaxLongitude >= left.MinLongitude
            && right.MinLongitude <= left.MaxLongitude;
    }

    private static bool TryPruneBoundarySliverSplit(
        IReadOnlyList<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> clippedSurfaces,
        out IReadOnlyList<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> prunedSurfaces)
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

        List<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> keptSurfaces =
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
            bool isBoundarySliver = IsBoundarySliver(metrics[index], areaRatio);
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

    private static bool TryPruneBoundarySliverGroups(
        List<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> surfaces,
        out IReadOnlyList<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> prunedSurfaces)
    {
        prunedSurfaces = [];
        if (surfaces.Count <= 1)
        {
            return false;
        }

        GroupMetrics[] groups = surfaces
            .GroupBy(static surface => surface.Overlay)
            .Select(static group =>
            {
                (ParsedSurface Surface, TerrainTextureOverlay Overlay)[] groupSurfaces = group.ToArray();
                SurfaceMetrics[] metrics = groupSurfaces
                    .Select(static entry => ComputeSurfaceMetrics(entry.Surface))
                    .ToArray();
                return new GroupMetrics(
                    group.Key,
                    groupSurfaces,
                    metrics.Sum(static metric => metric.AreaSquareMeters),
                    metrics);
            })
            .ToArray();
        if (groups.Length <= 1)
        {
            return false;
        }

        double totalArea = groups.Sum(static group => group.AreaSquareMeters);
        if (totalArea <= 1e-9)
        {
            return false;
        }

        int dominantIndex = 0;
        for (int index = 1; index < groups.Length; index++)
        {
            if (groups[index].AreaSquareMeters > groups[dominantIndex].AreaSquareMeters)
            {
                dominantIndex = index;
            }
        }

        List<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> keptSurfaces = [];
        bool prunedBoundarySliver = false;
        for (int index = 0; index < groups.Length; index++)
        {
            GroupMetrics group = groups[index];
            if (index != dominantIndex)
            {
                double areaRatio = group.AreaSquareMeters / totalArea;
                bool isBoundarySliverGroup = group.SurfaceMetrics.Count > 0
                    && group.SurfaceMetrics.All(metric => IsBoundarySliver(metric, areaRatio));
                if (isBoundarySliverGroup)
                {
                    prunedBoundarySliver = true;
                    continue;
                }
            }

            keptSurfaces.AddRange(group.Surfaces);
        }

        if (!prunedBoundarySliver || keptSurfaces.Count == surfaces.Count || keptSurfaces.Count == 0)
        {
            return false;
        }

        prunedSurfaces = keptSurfaces
            .OrderBy(static entry => entry.Overlay.PackageName, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Overlay.GeographicBounds.MinLatitude)
            .ThenBy(static entry => entry.Overlay.GeographicBounds.MinLongitude)
            .ThenBy(static entry => entry.Surface.PolygonId, StringComparer.Ordinal)
            .ToArray();
        return true;
    }

    private static GeodeticPoint GetCityObjectOrigin(
        ParsedCityObject cityObject)
    {
        if (cityObject.GeodeticOriginOverride is not null)
        {
            return cityObject.GeodeticOriginOverride!;
        }

        bool hasPoint = false;
        GeodeticPoint? origin = null;
        foreach (ParsedSurface surface in cityObject.Surfaces)
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

    private static GeographicRectangle GetSurfaceGeographicBounds(
        ParsedSurface surface)
    {
        return new GeographicRectangle(
            MinLatitude: surface.Vertices.Min(static point => point.Latitude),
            MaxLatitude: surface.Vertices.Max(static point => point.Latitude),
            MinLongitude: surface.Vertices.Min(static point => point.Longitude),
            MaxLongitude: surface.Vertices.Max(static point => point.Longitude));
    }

    private static SurfaceMetrics ComputeSurfaceMetrics(ParsedSurface surface)
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

    private static bool IsBoundarySliver(SurfaceMetrics metrics, double areaRatio)
    {
        return metrics.EstimatedThicknessMeters <= BoundarySliverMaxThicknessMeters
            && (areaRatio <= BoundarySliverMaxAreaRatio
                || metrics.AreaSquareMeters <= BoundarySliverMaxAreaSquareMeters);
    }

    private static double Distance(ProjectedPoint left, ProjectedPoint right)
    {
        double deltaX = right.X - left.X;
        double deltaY = right.Y - left.Y;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }

    private readonly record struct ProjectedPoint(double X, double Y);

    private readonly record struct SurfaceMetrics(double AreaSquareMeters, double EstimatedThicknessMeters);

    private readonly record struct TerrainOverlayCoverage(
        TerrainOverlayCoverageKind Kind,
        TerrainTextureOverlay? ContainingOverlay,
        IReadOnlyList<TerrainTextureOverlay> IntersectingOverlays)
    {
        public static TerrainOverlayCoverage None { get; } = new(
            TerrainOverlayCoverageKind.None,
            ContainingOverlay: null,
            IntersectingOverlays: []);

        public static TerrainOverlayCoverage Contained(TerrainTextureOverlay containingOverlay)
        {
            return new TerrainOverlayCoverage(
                TerrainOverlayCoverageKind.Contained,
                containingOverlay,
                IntersectingOverlays: []);
        }

        public static TerrainOverlayCoverage Intersecting(IReadOnlyList<TerrainTextureOverlay> intersectingOverlays)
        {
            return new TerrainOverlayCoverage(
                TerrainOverlayCoverageKind.Intersecting,
                ContainingOverlay: null,
                intersectingOverlays);
        }
    }

    private enum TerrainOverlayCoverageKind
    {
        None,
        Contained,
        Intersecting,
    }

    private readonly record struct GroupMetrics(
        TerrainTextureOverlay Overlay,
        IReadOnlyList<(ParsedSurface Surface, TerrainTextureOverlay Overlay)> Surfaces,
        double AreaSquareMeters,
        IReadOnlyList<SurfaceMetrics> SurfaceMetrics);
}
