using System;
using System.Collections.Generic;
using System.Linq;

using NetTopologySuite.Geometries;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class DemTerrainOverlaySurfaceClipper
{
    private static readonly GeometryFactory GeometryFactory = new();
    private readonly record struct ResolvedSurfaceVertex(
        GeodeticPoint Point,
        Float2? UV);

    public static IReadOnlyList<(BootstrapParsedSurface Surface, TerrainTextureOverlay Overlay)> ClipGeneratedSurfaceToOverlays(
        BootstrapParsedSurface surface,
        IReadOnlyList<TerrainTextureOverlay> overlays)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(overlays);

        List<(BootstrapParsedSurface Surface, TerrainTextureOverlay Overlay)> results = [];
        foreach (TerrainTextureOverlay overlay in overlays)
        {
            foreach (BootstrapParsedSurface clippedSurface in ClipSurfaceCore(
                         surface,
                         [overlay.GeographicBounds],
                         suffixFactory: (_, localPolygonIndex) => $"{CreateOverlayToken(overlay.GeographicBounds)}_{localPolygonIndex:D2}"))
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
        IReadOnlyList<GeographicRectangle> bounds)
    {
        return ClipSurfaceToBounds(surface, bounds);
    }

    public static IReadOnlyList<BootstrapParsedSurface> ClipSurfaceToBounds(
        BootstrapParsedSurface surface,
        IReadOnlyList<GeographicRectangle> bounds)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(bounds);

        return ClipSurfaceCore(
            surface,
            bounds,
            suffixFactory: (boundIndex, polygonIndex) => $"{CreateOverlayToken(bounds[boundIndex])}_{boundIndex:D2}_{polygonIndex:D2}");
    }

    private static List<BootstrapParsedSurface> ClipSurfaceCore(
        BootstrapParsedSurface surface,
        IReadOnlyList<GeographicRectangle> bounds,
        Func<int, int, string> suffixFactory)
    {
        List<BootstrapParsedSurface> results = [];
        for (int boundIndex = 0; boundIndex < bounds.Count; boundIndex++)
        {
            int polygonIndex = 0;
            foreach (Polygon polygon in ClipToOverlay(surface, bounds[boundIndex]))
            {
                ResolvedSurfaceVertex[] resolvedVertices = polygon.Coordinates
                    .Take(Math.Max(polygon.Coordinates.Length - 1, 0))
                    .Select(coordinate => ResolveSurfaceVertex(surface, coordinate))
                    .ToArray();
                (GeodeticPoint[] vertices, IReadOnlyList<Float2>? uvs) =
                    NormalizeResolvedVertices(surface, resolvedVertices);
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

    private static IEnumerable<Polygon> ClipToOverlay(
        BootstrapParsedSurface surface,
        GeographicRectangle rectangle)
    {
        if (surface.InteriorRings.Length != 0 || surface.ExteriorRing.Vertices.Length < 3)
        {
            yield break;
        }

        Polygon input = GeometryFactory.CreatePolygon(ToClosedCoordinates(surface.ExteriorRing.Vertices));
        Polygon clip = GeometryFactory.CreatePolygon(
        [
            new Coordinate(rectangle.MinLongitude, rectangle.MinLatitude),
            new Coordinate(rectangle.MaxLongitude, rectangle.MinLatitude),
            new Coordinate(rectangle.MaxLongitude, rectangle.MaxLatitude),
            new Coordinate(rectangle.MinLongitude, rectangle.MaxLatitude),
            new Coordinate(rectangle.MinLongitude, rectangle.MinLatitude),
        ]);

        Geometry intersection = input.Intersection(clip);
        foreach (Geometry geometry in EnumeratePolygons(intersection))
        {
            if (geometry is Polygon polygon && !polygon.IsEmpty)
            {
                yield return polygon;
            }
        }
    }

    private static IEnumerable<Geometry> EnumeratePolygons(Geometry geometry)
    {
        if (geometry.IsEmpty)
        {
            yield break;
        }

        if (geometry is Polygon)
        {
            yield return geometry;
            yield break;
        }

        if (geometry is MultiPolygon or GeometryCollection)
        {
            for (int index = 0; index < geometry.NumGeometries; index++)
            {
                foreach (Geometry child in EnumeratePolygons(geometry.GetGeometryN(index)))
                {
                    yield return child;
                }
            }
        }
    }

    private static Coordinate[] ToClosedCoordinates(GeodeticPoint[] vertices)
    {
        Coordinate[] coordinates = new Coordinate[vertices.Length + 1];
        for (int index = 0; index < vertices.Length; index++)
        {
            coordinates[index] = new Coordinate(vertices[index].Longitude, vertices[index].Latitude);
        }

        coordinates[^1] = new Coordinate(vertices[0].Longitude, vertices[0].Latitude);
        return coordinates;
    }

    private static ResolvedSurfaceVertex ResolveSurfaceVertex(
        BootstrapParsedSurface surface,
        Coordinate coordinate)
    {
        GeodeticPoint[] vertices = surface.ExteriorRing.Vertices;
        IReadOnlyList<Float2>? uvs = surface.ExteriorRing.UVs;
        for (int index = 0; index < vertices.Length; index++)
        {
            if (Approximately(vertices[index].Longitude, coordinate.X)
                && Approximately(vertices[index].Latitude, coordinate.Y))
            {
                return new ResolvedSurfaceVertex(vertices[index], uvs is not null && index < uvs.Count ? uvs[index] : null);
            }
        }

        if (TryResolveEdgePoint(vertices, uvs, coordinate, out ResolvedSurfaceVertex edgePoint))
        {
            return edgePoint;
        }

        return ResolvePlanarPoint(vertices, uvs, coordinate);
    }

    private static bool TryResolveEdgePoint(
        GeodeticPoint[] vertices,
        IReadOnlyList<Float2>? uvs,
        Coordinate coordinate,
        out ResolvedSurfaceVertex point)
    {
        for (int index = 0; index < vertices.Length; index++)
        {
            GeodeticPoint start = vertices[index];
            GeodeticPoint end = vertices[(index + 1) % vertices.Length];
            double deltaLongitude = end.Longitude - start.Longitude;
            double deltaLatitude = end.Latitude - start.Latitude;
            double edgeLengthSquared = (deltaLongitude * deltaLongitude) + (deltaLatitude * deltaLatitude);
            if (edgeLengthSquared <= 1e-18)
            {
                continue;
            }

            double ratio = ((coordinate.X - start.Longitude) * deltaLongitude + (coordinate.Y - start.Latitude) * deltaLatitude) / edgeLengthSquared;
            if (ratio < -1e-8 || ratio > 1.0 + 1e-8)
            {
                continue;
            }

            double projectedLongitude = start.Longitude + (deltaLongitude * ratio);
            double projectedLatitude = start.Latitude + (deltaLatitude * ratio);
            if (!Approximately(projectedLongitude, coordinate.X) || !Approximately(projectedLatitude, coordinate.Y))
            {
                continue;
            }

            ratio = Math.Clamp(ratio, 0.0, 1.0);
            Float2? uv = null;
            if (uvs is not null
                && index < uvs.Count
                && ((index + 1) % vertices.Length) < uvs.Count)
            {
                Float2 startUv = uvs[index];
                Float2 endUv = uvs[(index + 1) % vertices.Length];
                uv = new Float2(
                    startUv.X + ((endUv.X - startUv.X) * ratio),
                    startUv.Y + ((endUv.Y - startUv.Y) * ratio));
            }

            point = new ResolvedSurfaceVertex(
                new GeodeticPoint(
                    coordinate.Y,
                    coordinate.X,
                    start.Altitude + ((end.Altitude - start.Altitude) * ratio)),
                uv);
            return true;
        }

        point = default;
        return false;
    }

    private static ResolvedSurfaceVertex ResolvePlanarPoint(
        GeodeticPoint[] vertices,
        IReadOnlyList<Float2>? uvs,
        Coordinate coordinate)
    {
        GeodeticPoint origin = vertices[0];
        for (int index = 1; index + 1 < vertices.Length; index++)
        {
            Float2? originUv = uvs is not null && 0 < uvs.Count ? uvs[0] : null;
            Float2? vertexUv = uvs is not null && index < uvs.Count ? uvs[index] : null;
            Float2? nextUv = uvs is not null && index + 1 < uvs.Count ? uvs[index + 1] : null;
            if (TryResolveTrianglePoint(
                    origin,
                    vertices[index],
                    vertices[index + 1],
                    originUv,
                    vertexUv,
                    nextUv,
                    coordinate,
                    out ResolvedSurfaceVertex point))
            {
                return point;
            }
        }

        return new ResolvedSurfaceVertex(
            new GeodeticPoint(coordinate.Y, coordinate.X, origin.Altitude),
            uvs is not null && uvs.Count > 0 ? uvs[0] : null);
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

    private static bool TryResolveTrianglePoint(
        GeodeticPoint a,
        GeodeticPoint b,
        GeodeticPoint c,
        Float2? uvA,
        Float2? uvB,
        Float2? uvC,
        Coordinate coordinate,
        out ResolvedSurfaceVertex point)
    {
        double denominator =
            ((b.Latitude - c.Latitude) * (a.Longitude - c.Longitude))
            + ((c.Longitude - b.Longitude) * (a.Latitude - c.Latitude));
        if (Math.Abs(denominator) <= 1e-18)
        {
            point = default;
            return false;
        }

        double weightA =
            (((b.Latitude - c.Latitude) * (coordinate.X - c.Longitude))
             + ((c.Longitude - b.Longitude) * (coordinate.Y - c.Latitude)))
            / denominator;
        double weightB =
            (((c.Latitude - a.Latitude) * (coordinate.X - c.Longitude))
             + ((a.Longitude - c.Longitude) * (coordinate.Y - c.Latitude)))
            / denominator;
        double weightC = 1.0 - weightA - weightB;

        if (weightA < -1e-8 || weightB < -1e-8 || weightC < -1e-8)
        {
            point = default;
            return false;
        }

        double altitude = (a.Altitude * weightA) + (b.Altitude * weightB) + (c.Altitude * weightC);
        Float2? uv = null;
        if (uvA is not null && uvB is not null && uvC is not null)
        {
            uv = new Float2(
                (uvA.X * weightA) + (uvB.X * weightB) + (uvC.X * weightC),
                (uvA.Y * weightA) + (uvB.Y * weightB) + (uvC.Y * weightC));
        }

        point = new ResolvedSurfaceVertex(
            new GeodeticPoint(coordinate.Y, coordinate.X, altitude),
            uv);
        return true;
    }

    private static bool Approximately(double left, double right)
    {
        return Math.Abs(left - right) <= 1e-9;
    }

    private static string CreateOverlayToken(GeographicRectangle rectangle)
    {
        long south = (long)Math.Round(rectangle.MinLatitude * 1_000_000.0, MidpointRounding.AwayFromZero);
        long west = (long)Math.Round(rectangle.MinLongitude * 1_000_000.0, MidpointRounding.AwayFromZero);
        return $"{south}_{west}";
    }
}
