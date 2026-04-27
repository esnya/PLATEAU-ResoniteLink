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

        foreach (ClipBoundary boundary in Enum.GetValues<ClipBoundary>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            vertices = ClipAgainstBoundary(vertices, rectangle, boundary);
            if (vertices.Count < 3)
            {
                yield break;
            }
        }

        yield return vertices;
    }

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
