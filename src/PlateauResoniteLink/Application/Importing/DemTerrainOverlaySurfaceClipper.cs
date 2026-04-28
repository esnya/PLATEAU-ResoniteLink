using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class DemTerrainOverlaySurfaceClipper
{
    private const double ClipEpsilon = 1e-12;
    private const long ClipProgressReportInterval = 10_000;
    private const double ClipVertexQuantization = 1_000_000_000.0;

    private static long clipProgressCounter;

    private readonly record struct ResolvedSurfaceVertex(
        GeodeticPoint Point,
        Float2? UV);

    private enum ClipBoundary
    {
        West,
        East,
        South,
        North,
    }

    public static IReadOnlyList<(BootstrapParsedSurface Surface, TerrainTextureOverlay Overlay)> ClipGeneratedSurfaceToOverlays(
        BootstrapParsedSurface surface,
        IReadOnlyList<TerrainTextureOverlay> overlays,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(overlays);

        List<(BootstrapParsedSurface Surface, TerrainTextureOverlay Overlay)> results = [];
        foreach (TerrainTextureOverlay overlay in overlays)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (BootstrapParsedSurface clippedSurface in ClipSurfaceCore(
                         surface,
                         [overlay.GeographicBounds],
                         suffixFactory: (_, localPolygonIndex) => $"{CreateOverlayToken(overlay.GeographicBounds)}_{localPolygonIndex:D2}",
                         progressReporter,
                         cancellationToken))
            {
                results.Add((
                    clippedSurface,
                    overlay));
            }
        }

        return results;
    }

    public static IReadOnlyList<BootstrapParsedSurface> ClipGeneratedSurfaceToBounds(
        BootstrapParsedSurface surface,
        IReadOnlyList<GeographicRectangle> bounds,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return ClipSurfaceToBounds(surface, bounds, progressReporter, cancellationToken);
    }

    public static IReadOnlyList<BootstrapParsedSurface> ClipSurfaceToBounds(
        BootstrapParsedSurface surface,
        IReadOnlyList<GeographicRectangle> bounds,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(bounds);

        return ClipSurfaceCore(
            surface,
            bounds,
            suffixFactory: (boundIndex, polygonIndex) => $"{CreateOverlayToken(bounds[boundIndex])}_{boundIndex:D2}_{polygonIndex:D2}",
            progressReporter,
            cancellationToken);
    }

    private static List<BootstrapParsedSurface> ClipSurfaceCore(
        BootstrapParsedSurface surface,
        IReadOnlyList<GeographicRectangle> bounds,
        Func<int, int, string> suffixFactory,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<BootstrapParsedSurface> results = [];
        if (surface.ExteriorRing.Vertices.Length < 3)
        {
            return results;
        }

        GeographicRectangle surfaceBounds = GetSurfaceBounds(surface);
        for (int boundIndex = 0; boundIndex < bounds.Count; boundIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GeographicRectangle bound = bounds[boundIndex];
            if (!Intersects(surfaceBounds, bound))
            {
                continue;
            }

            if (ShouldReportClipProgress(boundIndex, bounds.Count))
            {
                progressReporter?.Invoke(
                    PlateauLog.Debug(
                        "import",
                        $"Clipping DEM surface '{surface.PolygonId}' to geographic bound "
                        + $"{boundIndex + 1}/{bounds.Count} "
                        + $"(vertices={surface.ExteriorRing.Vertices.Length})."));
            }

            int polygonIndex = 0;
            foreach (IReadOnlyList<ResolvedSurfaceVertex> polygon in ClipToOverlay(surface, bound, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                (GeodeticPoint[] vertices, IReadOnlyList<Float2>? uvs) =
                    NormalizeResolvedVertices(surface, polygon);
                if (vertices.Length < 3)
                {
                    continue;
                }

                string suffix = suffixFactory(boundIndex, polygonIndex);
                results.Add(surface with
                {
                    PolygonId = $"{surface.PolygonId}_{suffix}",
                    ExteriorRing = new BootstrapParsedRing(
                        $"{surface.ExteriorRing.RingId}_{suffix}",
                        vertices,
                        uvs),
                    UsesGeneratedDemTexture = surface.UsesGeneratedDemTexture,
                });
                polygonIndex++;
            }
        }

        return results;
    }

    private static GeographicRectangle GetSurfaceBounds(BootstrapParsedSurface surface)
    {
        return new GeographicRectangle(
            MinLatitude: surface.ExteriorRing.Vertices.Min(static point => point.Latitude),
            MaxLatitude: surface.ExteriorRing.Vertices.Max(static point => point.Latitude),
            MinLongitude: surface.ExteriorRing.Vertices.Min(static point => point.Longitude),
            MaxLongitude: surface.ExteriorRing.Vertices.Max(static point => point.Longitude));
    }

    private static bool Intersects(GeographicRectangle left, GeographicRectangle right)
    {
        return left.MaxLatitude >= right.MinLatitude
            && left.MinLatitude <= right.MaxLatitude
            && left.MaxLongitude >= right.MinLongitude
            && left.MinLongitude <= right.MaxLongitude;
    }

    private static bool ShouldReportClipProgress(int boundIndex, int boundCount)
    {
        _ = boundIndex;
        _ = boundCount;
        return Interlocked.Increment(ref clipProgressCounter) % ClipProgressReportInterval == 0;
    }

    private static IEnumerable<IReadOnlyList<ResolvedSurfaceVertex>> ClipToOverlay(
        BootstrapParsedSurface surface,
        GeographicRectangle rectangle,
        CancellationToken cancellationToken)
    {
        if (surface.InteriorRings.Length != 0 || surface.ExteriorRing.Vertices.Length < 3)
        {
            yield break;
        }

        cancellationToken.ThrowIfCancellationRequested();
        List<ResolvedSurfaceVertex> vertices = surface.ExteriorRing.Vertices
            .Select((point, index) => new ResolvedSurfaceVertex(
                point,
                surface.ExteriorRing.UVs is not null && index < surface.ExteriorRing.UVs.Count
                    ? surface.ExteriorRing.UVs[index]
                    : null))
            .ToList();

        if (!IsConcave(vertices))
        {
            foreach (IReadOnlyList<ResolvedSurfaceVertex> polygon in ClipPolygonToRectangle(vertices, rectangle, cancellationToken))
            {
                yield return polygon;
            }

            yield break;
        }

        List<IReadOnlyList<ResolvedSurfaceVertex>> triangleClipped = [];
        foreach (IReadOnlyList<ResolvedSurfaceVertex> triangle in Triangulate(vertices, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (IReadOnlyList<ResolvedSurfaceVertex> polygon in ClipPolygonToRectangle(triangle, rectangle, cancellationToken))
            {
                triangleClipped.Add(polygon);
            }
        }

        List<List<ResolvedSurfaceVertex>>? mergedPolygons = TryMergeClippedTriangleComponents(
            triangleClipped,
            cancellationToken);
        IEnumerable<IReadOnlyList<ResolvedSurfaceVertex>> clippedPolygons = mergedPolygons is null
            ? triangleClipped
            : mergedPolygons;
        foreach (IReadOnlyList<ResolvedSurfaceVertex> polygon in clippedPolygons)
        {
            if (polygon.Count >= 3)
            {
                yield return polygon;
            }
        }
    }

    private static IEnumerable<IReadOnlyList<ResolvedSurfaceVertex>> ClipPolygonToRectangle(
        IReadOnlyList<ResolvedSurfaceVertex> vertices,
        GeographicRectangle rectangle,
        CancellationToken cancellationToken)
    {
        List<ResolvedSurfaceVertex> clipped = [.. vertices];
        foreach (ClipBoundary boundary in Enum.GetValues<ClipBoundary>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            clipped = ClipAgainstBoundary(clipped, rectangle, boundary);
            if (clipped.Count < 3)
            {
                yield break;
            }
        }

        if (clipped.Count >= 3)
        {
            yield return clipped;
        }
    }

    private static bool IsConcave(List<ResolvedSurfaceVertex> vertices)
    {
        if (vertices.Count < 4)
        {
            return false;
        }

        double polygonSignedArea = ComputeSignedArea(vertices.Select(static vertex => vertex.Point).ToArray());
        if (Math.Abs(polygonSignedArea) <= ClipEpsilon)
        {
            return false;
        }

        double sign = Math.Sign(polygonSignedArea);
        for (int index = 0; index < vertices.Count; index++)
        {
            GeodeticPoint previous = vertices[(index - 1 + vertices.Count) % vertices.Count].Point;
            GeodeticPoint current = vertices[index].Point;
            GeodeticPoint next = vertices[(index + 1) % vertices.Count].Point;
            if (Math.Sign(Cross(previous, current, next) * sign) < 0.0)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<IReadOnlyList<ResolvedSurfaceVertex>> Triangulate(
        List<ResolvedSurfaceVertex> vertices,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<ResolvedSurfaceVertex> sanitizedVertices = [];
        foreach (ResolvedSurfaceVertex vertex in vertices)
        {
            if (sanitizedVertices.Count > 0 && sanitizedVertices[^1].Point.Equals(vertex.Point))
            {
                continue;
            }

            sanitizedVertices.Add(vertex);
        }

        if (sanitizedVertices.Count < 3)
        {
            yield break;
        }

        double sourceSignedArea = ComputeSignedArea(sanitizedVertices.Select(static vertex => vertex.Point).ToArray());
        if (Math.Abs(sourceSignedArea) <= ClipEpsilon)
        {
            yield break;
        }

        List<int> remaining = Enumerable.Range(0, sanitizedVertices.Count).ToList();
        double signedAreaSign = Math.Sign(sourceSignedArea);
        int safety = sanitizedVertices.Count * sanitizedVertices.Count * 4;
        while (remaining.Count > 2 && safety > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool clippedEar = false;
            for (int earIndex = 0; earIndex < remaining.Count; earIndex++)
            {
                int previous = remaining[(earIndex - 1 + remaining.Count) % remaining.Count];
                int current = remaining[earIndex];
                int next = remaining[(earIndex + 1) % remaining.Count];
                GeodeticPoint previousVertex = sanitizedVertices[previous].Point;
                GeodeticPoint currentVertex = sanitizedVertices[current].Point;
                GeodeticPoint nextVertex = sanitizedVertices[next].Point;

                if (!IsConvexEar(previousVertex, currentVertex, nextVertex, signedAreaSign))
                {
                    continue;
                }

                bool containsPoint = false;
                for (int insideIndex = 0; insideIndex < remaining.Count; insideIndex++)
                {
                    int candidateIndex = remaining[insideIndex];
                    if (candidateIndex == previous || candidateIndex == current || candidateIndex == next)
                    {
                        continue;
                    }

                    if (IsPointInTriangle(
                            sanitizedVertices[candidateIndex].Point,
                            previousVertex,
                            currentVertex,
                            nextVertex,
                            signedAreaSign))
                    {
                        containsPoint = true;
                        break;
                    }
                }

                if (containsPoint)
                {
                    continue;
                }

                yield return new[]
                {
                    sanitizedVertices[previous],
                    sanitizedVertices[current],
                    sanitizedVertices[next],
                };
                remaining.RemoveAt(earIndex);
                clippedEar = true;
                break;
            }

            if (!clippedEar)
            {
                yield break;
            }

            safety--;
        }
    }

    private static bool IsConvexEar(
        GeodeticPoint previous,
        GeodeticPoint current,
        GeodeticPoint next,
        double signedAreaSign)
    {
        double cross = Cross(previous, current, next);
        if (Math.Abs(cross) <= ClipEpsilon)
        {
            return false;
        }

        return Math.Sign(cross) == Math.Sign(signedAreaSign);
    }

    private static bool IsPointInTriangle(
        GeodeticPoint point,
        GeodeticPoint a,
        GeodeticPoint b,
        GeodeticPoint c,
        double signedAreaSign)
    {
        double ab = Cross(a, b, point);
        double bc = Cross(b, c, point);
        double ca = Cross(c, a, point);

        return signedAreaSign > 0.0
            ? ab >= -ClipEpsilon && bc >= -ClipEpsilon && ca >= -ClipEpsilon
            : ab <= ClipEpsilon && bc <= ClipEpsilon && ca <= ClipEpsilon;
    }

    private static List<List<ResolvedSurfaceVertex>>? TryMergeClippedTriangleComponents(
        IReadOnlyList<IReadOnlyList<ResolvedSurfaceVertex>> polygons,
        CancellationToken cancellationToken)
    {
        if (polygons.Count == 0)
        {
            return [];
        }

        Dictionary<DirectedEdgeKey, DirectedEdgeValue> edges = [];
        Dictionary<PointKey, ResolvedSurfaceVertex> points = [];

        foreach (IReadOnlyList<ResolvedSurfaceVertex> polygon in polygons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (polygon.Count < 3)
            {
                continue;
            }

            for (int index = 0; index < polygon.Count; index++)
            {
                ResolvedSurfaceVertex from = polygon[index];
                ResolvedSurfaceVertex to = polygon[(index + 1) % polygon.Count];
                if (from.Point.Equals(to.Point))
                {
                    continue;
                }

                PointKey fromKey = CreatePointKey(from.Point);
                PointKey toKey = CreatePointKey(to.Point);
                DirectedEdgeKey key = new(fromKey, toKey);
                DirectedEdgeKey reverseKey = new(toKey, fromKey);
                if (edges.Remove(reverseKey))
                {
                    continue;
                }

                edges[key] = new DirectedEdgeValue(from, to);
                points.TryAdd(fromKey, from);
                points.TryAdd(toKey, to);
            }
        }

        if (edges.Count == 0)
        {
            return null;
        }

        Dictionary<PointKey, List<PointKey>> adjacency = [];
        foreach (DirectedEdgeKey edge in edges.Keys)
        {
            if (!adjacency.TryGetValue(edge.From, out List<PointKey>? values))
            {
                values = [];
                adjacency[edge.From] = values;
            }

            values.Add(edge.To);
        }

        foreach (List<PointKey> list in adjacency.Values)
        {
            list.Sort();
        }

        HashSet<DirectedEdgeKey> usedEdges = [];
        List<List<ResolvedSurfaceVertex>> merged = [];

        foreach (DirectedEdgeKey startEdge in edges.Keys.OrderBy(static edge => edge.From).ThenBy(static edge => edge.To))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (usedEdges.Contains(startEdge))
            {
                continue;
            }

            List<ResolvedSurfaceVertex> ring = [];
            PointKey start = startEdge.From;
            PointKey current = start;
            PointKey next = startEdge.To;
            ring.Add(points[start]);
            int safety = edges.Count + 2;

            while (safety > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DirectedEdgeKey edge = new(current, next);
                if (!edges.TryGetValue(edge, out DirectedEdgeValue edgeValue))
                {
                    break;
                }

                if (!usedEdges.Add(edge))
                {
                    break;
                }

                ring.Add(edgeValue.To);
                current = next;
                if (current.Equals(start))
                {
                    break;
                }

                if (!adjacency.TryGetValue(current, out List<PointKey>? nextCandidates)
                    || nextCandidates.Count == 0)
                {
                    break;
                }

                PointKey? nextCandidate = null;
                foreach (PointKey candidate in nextCandidates)
                {
                    if (!usedEdges.Contains(new(current, candidate)))
                    {
                        nextCandidate = candidate;
                        break;
                    }
                }

                if (nextCandidate is null)
                {
                    break;
                }

                next = nextCandidate.Value;
                safety--;
            }

            if (current != start || ring.Count < 3)
            {
                continue;
            }

            List<ResolvedSurfaceVertex>? normalized = NormalizeResolvedVerticesFromRing(ring);
            if (normalized is null || normalized.Count < 3)
            {
                continue;
            }

            double signedArea = ComputeSignedArea(normalized.Select(static vertex => vertex.Point).ToArray());
            if (Math.Abs(signedArea) <= ClipEpsilon)
            {
                continue;
            }

            merged.Add(normalized);
        }

        return merged.Count > 0 && usedEdges.Count == edges.Count
            ? merged
            : null;
    }

    private static List<ResolvedSurfaceVertex>? NormalizeResolvedVerticesFromRing(
        IReadOnlyList<ResolvedSurfaceVertex> ringVertices)
    {
        if (ringVertices.Count < 3)
        {
            return null;
        }

        List<ResolvedSurfaceVertex> normalized = [];
        foreach (ResolvedSurfaceVertex vertex in ringVertices)
        {
            if (normalized.Count > 0 && normalized[^1].Point.Equals(vertex.Point))
            {
                continue;
            }

            normalized.Add(vertex);
        }

        if (normalized.Count == 0)
        {
            return null;
        }

        if (normalized.Count > 1 && normalized[0].Point.Equals(normalized[^1].Point))
        {
            normalized.RemoveAt(normalized.Count - 1);
        }

        if (normalized.Count < 3)
        {
            return null;
        }

        return normalized;
    }

    private static PointKey CreatePointKey(GeodeticPoint point)
    {
        return new(
            (long)Math.Round(point.Longitude * ClipVertexQuantization),
            (long)Math.Round(point.Latitude * ClipVertexQuantization));
    }

    private static double Cross(GeodeticPoint from, GeodeticPoint through, GeodeticPoint to)
    {
        return (through.Longitude - from.Longitude) * (to.Latitude - from.Latitude)
            - (through.Latitude - from.Latitude) * (to.Longitude - from.Longitude);
    }

    private readonly record struct PointKey(long LongitudeKey, long LatitudeKey) : IComparable<PointKey>
    {
        public int CompareTo(PointKey other)
        {
            int longitudeComparison = LongitudeKey.CompareTo(other.LongitudeKey);
            return longitudeComparison != 0 ? longitudeComparison : LatitudeKey.CompareTo(other.LatitudeKey);
        }
    }

    private readonly record struct DirectedEdgeKey(PointKey From, PointKey To);

    private readonly record struct DirectedEdgeValue(ResolvedSurfaceVertex From, ResolvedSurfaceVertex To);

    private static List<ResolvedSurfaceVertex> ClipAgainstBoundary(
        IReadOnlyList<ResolvedSurfaceVertex> vertices,
        GeographicRectangle rectangle,
        ClipBoundary boundary)
    {
        List<ResolvedSurfaceVertex> output = [];
        ResolvedSurfaceVertex previous = vertices[^1];
        bool previousInside = IsInside(previous, rectangle, boundary);

        foreach (ResolvedSurfaceVertex current in vertices)
        {
            bool currentInside = IsInside(current, rectangle, boundary);
            if (currentInside)
            {
                if (!previousInside)
                {
                    output.Add(IntersectBoundary(previous, current, rectangle, boundary));
                }

                output.Add(current);
            }
            else if (previousInside)
            {
                output.Add(IntersectBoundary(previous, current, rectangle, boundary));
            }

            previous = current;
            previousInside = currentInside;
        }

        return output;
    }

    private static bool IsInside(
        ResolvedSurfaceVertex vertex,
        GeographicRectangle rectangle,
        ClipBoundary boundary)
    {
        return boundary switch
        {
            ClipBoundary.West => vertex.Point.Longitude >= rectangle.MinLongitude - ClipEpsilon,
            ClipBoundary.East => vertex.Point.Longitude <= rectangle.MaxLongitude + ClipEpsilon,
            ClipBoundary.South => vertex.Point.Latitude >= rectangle.MinLatitude - ClipEpsilon,
            ClipBoundary.North => vertex.Point.Latitude <= rectangle.MaxLatitude + ClipEpsilon,
            _ => throw new ArgumentOutOfRangeException(nameof(boundary), boundary, null),
        };
    }

    private static ResolvedSurfaceVertex IntersectBoundary(
        ResolvedSurfaceVertex start,
        ResolvedSurfaceVertex end,
        GeographicRectangle rectangle,
        ClipBoundary boundary)
    {
        double denominator = boundary is ClipBoundary.West or ClipBoundary.East
            ? end.Point.Longitude - start.Point.Longitude
            : end.Point.Latitude - start.Point.Latitude;
        if (Math.Abs(denominator) <= ClipEpsilon)
        {
            return start;
        }

        double value = boundary switch
        {
            ClipBoundary.West => rectangle.MinLongitude,
            ClipBoundary.East => rectangle.MaxLongitude,
            ClipBoundary.South => rectangle.MinLatitude,
            ClipBoundary.North => rectangle.MaxLatitude,
            _ => throw new ArgumentOutOfRangeException(nameof(boundary), boundary, null),
        };
        double sourceValue = boundary is ClipBoundary.West or ClipBoundary.East
            ? start.Point.Longitude
            : start.Point.Latitude;
        double ratio = Math.Clamp((value - sourceValue) / denominator, 0.0, 1.0);

        Float2? uv = null;
        if (start.UV is not null && end.UV is not null)
        {
            Float2 startUv = start.UV;
            Float2 endUv = end.UV;
            uv = new Float2(
                startUv.X + ((endUv.X - startUv.X) * ratio),
                startUv.Y + ((endUv.Y - startUv.Y) * ratio));
        }

        return new ResolvedSurfaceVertex(
            new GeodeticPoint(
                start.Point.Latitude + ((end.Point.Latitude - start.Point.Latitude) * ratio),
                start.Point.Longitude + ((end.Point.Longitude - start.Point.Longitude) * ratio),
                start.Point.Altitude + ((end.Point.Altitude - start.Point.Altitude) * ratio)),
            uv);
    }

    private static (
        GeodeticPoint[] Vertices,
        IReadOnlyList<Float2>? Uvs) NormalizeResolvedVertices(
        BootstrapParsedSurface sourceSurface,
        IReadOnlyList<ResolvedSurfaceVertex> resolvedVertices)
    {
        List<ResolvedSurfaceVertex> normalized = [];
        foreach (ResolvedSurfaceVertex resolvedVertex in resolvedVertices)
        {
            if (normalized.Count > 0
                && normalized[^1].Point.Equals(resolvedVertex.Point))
            {
                continue;
            }

            normalized.Add(resolvedVertex);
        }

        if (normalized.Count > 1
            && normalized[0].Point.Equals(normalized[^1].Point))
        {
            normalized.RemoveAt(normalized.Count - 1);
        }

        GeodeticPoint[] vertices = PreserveSurfaceWinding(
            sourceSurface.ExteriorRing.Vertices,
            normalized.Select(static vertex => vertex.Point).ToArray());
        if (vertices.Length < 3)
        {
            return (vertices, null);
        }

        if (normalized.Count != vertices.Length)
        {
            normalized = vertices
                .Select(vertex => resolvedVertices.FirstOrDefault(resolved => resolved.Point.Equals(vertex)))
                .ToList();
        }

        bool hasAnyUv = normalized.Any(static vertex => vertex.UV is not null);
        IReadOnlyList<Float2>? uvs = hasAnyUv
            ? normalized.Select(static vertex => vertex.UV ?? new Float2(0.0, 0.0)).ToArray()
            : null;
        return (vertices, uvs);
    }

    private static GeodeticPoint[] PreserveSurfaceWinding(
        GeodeticPoint[] sourceVertices,
        GeodeticPoint[] clippedVertices)
    {
        if (clippedVertices.Length < 3)
        {
            return clippedVertices;
        }

        double sourceSignedArea = ComputeSignedArea(sourceVertices);
        double clippedSignedArea = ComputeSignedArea(clippedVertices);
        if (Math.Abs(sourceSignedArea) <= 1e-12 || Math.Abs(clippedSignedArea) <= 1e-12)
        {
            return clippedVertices;
        }

        if (Math.Sign(sourceSignedArea) == Math.Sign(clippedSignedArea))
        {
            return clippedVertices;
        }

        GeodeticPoint[] reversed = (GeodeticPoint[])clippedVertices.Clone();
        Array.Reverse(reversed);
        return reversed;
    }

    private static double ComputeSignedArea(GeodeticPoint[] vertices)
    {
        if (vertices.Length < 3)
        {
            return 0.0;
        }

        double signedArea = 0.0;
        for (int index = 0; index < vertices.Length; index++)
        {
            GeodeticPoint current = vertices[index];
            GeodeticPoint next = vertices[(index + 1) % vertices.Length];
            signedArea += (current.Longitude * next.Latitude) - (next.Longitude * current.Latitude);
        }

        return signedArea * 0.5;
    }

    private static string CreateOverlayToken(GeographicRectangle rectangle)
    {
        long south = (long)Math.Round(rectangle.MinLatitude * 1_000_000.0, MidpointRounding.AwayFromZero);
        long west = (long)Math.Round(rectangle.MinLongitude * 1_000_000.0, MidpointRounding.AwayFromZero);
        return $"{south}_{west}";
    }
}
