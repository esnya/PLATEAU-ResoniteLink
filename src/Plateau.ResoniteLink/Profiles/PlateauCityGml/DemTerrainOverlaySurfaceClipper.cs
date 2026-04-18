using NetTopologySuite.Geometries;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal static class DemTerrainOverlaySurfaceClipper
{
    private static readonly GeometryFactory GeometryFactory = new();

    public static IReadOnlyList<(LocalCityGmlResonitePlanBuilder.ParsedSurface Surface, TerrainTextureOverlay Overlay)> ClipGeneratedSurfaceToOverlays(
        LocalCityGmlResonitePlanBuilder.ParsedSurface surface,
        IReadOnlyList<TerrainTextureOverlay> overlays)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(overlays);

        List<(LocalCityGmlResonitePlanBuilder.ParsedSurface Surface, TerrainTextureOverlay Overlay)> results = [];
        foreach (TerrainTextureOverlay overlay in overlays)
        {
            int polygonIndex = 0;
            foreach (Polygon polygon in ClipToOverlay(surface, overlay.GeographicBounds))
            {
                LocalCityGmlResonitePlanBuilder.GeodeticPoint[] vertices = polygon.Coordinates
                    .Take(Math.Max(polygon.Coordinates.Length - 1, 0))
                    .Select(coordinate => ResolveSurfacePoint(surface, coordinate))
                    .Distinct()
                    .ToArray();
                vertices = PreserveSurfaceWinding(surface.ExteriorRing.Vertices, vertices);
                if (vertices.Length < 3)
                {
                    continue;
                }

                string suffix = $"{CreateOverlayToken(overlay.GeographicBounds)}_{polygonIndex:D2}";
                results.Add((
                    surface with
                    {
                        PolygonId = $"{surface.PolygonId}_{suffix}",
                        ExteriorRing = new LocalCityGmlResonitePlanBuilder.ParsedRing(
                            $"{surface.ExteriorRing.RingId}_{suffix}",
                            vertices,
                            UVs: null),
                        UsesGeneratedDemTexture = surface.UsesGeneratedDemTexture,
                    },
                    overlay));
                polygonIndex++;
            }
        }

        return results;
    }

    private static IEnumerable<Polygon> ClipToOverlay(
        LocalCityGmlResonitePlanBuilder.ParsedSurface surface,
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

    private static Coordinate[] ToClosedCoordinates(LocalCityGmlResonitePlanBuilder.GeodeticPoint[] vertices)
    {
        Coordinate[] coordinates = new Coordinate[vertices.Length + 1];
        for (int index = 0; index < vertices.Length; index++)
        {
            coordinates[index] = new Coordinate(vertices[index].Longitude, vertices[index].Latitude);
        }

        coordinates[^1] = new Coordinate(vertices[0].Longitude, vertices[0].Latitude);
        return coordinates;
    }

    private static LocalCityGmlResonitePlanBuilder.GeodeticPoint ResolveSurfacePoint(
        LocalCityGmlResonitePlanBuilder.ParsedSurface surface,
        Coordinate coordinate)
    {
        LocalCityGmlResonitePlanBuilder.GeodeticPoint[] vertices = surface.ExteriorRing.Vertices;
        for (int index = 0; index < vertices.Length; index++)
        {
            if (Approximately(vertices[index].Longitude, coordinate.X)
                && Approximately(vertices[index].Latitude, coordinate.Y))
            {
                return vertices[index];
            }
        }

        if (TryResolveEdgePoint(vertices, coordinate, out LocalCityGmlResonitePlanBuilder.GeodeticPoint edgePoint))
        {
            return edgePoint;
        }

        return ResolvePlanarPoint(vertices, coordinate);
    }

    private static bool TryResolveEdgePoint(
        LocalCityGmlResonitePlanBuilder.GeodeticPoint[] vertices,
        Coordinate coordinate,
        out LocalCityGmlResonitePlanBuilder.GeodeticPoint point)
    {
        for (int index = 0; index < vertices.Length; index++)
        {
            LocalCityGmlResonitePlanBuilder.GeodeticPoint start = vertices[index];
            LocalCityGmlResonitePlanBuilder.GeodeticPoint end = vertices[(index + 1) % vertices.Length];
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
            point = new LocalCityGmlResonitePlanBuilder.GeodeticPoint(
                coordinate.Y,
                coordinate.X,
                start.Altitude + ((end.Altitude - start.Altitude) * ratio));
            return true;
        }

        point = null!;
        return false;
    }

    private static LocalCityGmlResonitePlanBuilder.GeodeticPoint ResolvePlanarPoint(
        LocalCityGmlResonitePlanBuilder.GeodeticPoint[] vertices,
        Coordinate coordinate)
    {
        LocalCityGmlResonitePlanBuilder.GeodeticPoint origin = vertices[0];
        for (int index = 1; index + 1 < vertices.Length; index++)
        {
            if (TryResolveTrianglePoint(origin, vertices[index], vertices[index + 1], coordinate, out LocalCityGmlResonitePlanBuilder.GeodeticPoint point))
            {
                return point;
            }
        }

        return new LocalCityGmlResonitePlanBuilder.GeodeticPoint(coordinate.Y, coordinate.X, origin.Altitude);
    }

    private static LocalCityGmlResonitePlanBuilder.GeodeticPoint[] PreserveSurfaceWinding(
        LocalCityGmlResonitePlanBuilder.GeodeticPoint[] sourceVertices,
        LocalCityGmlResonitePlanBuilder.GeodeticPoint[] clippedVertices)
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

        LocalCityGmlResonitePlanBuilder.GeodeticPoint[] reversed = (LocalCityGmlResonitePlanBuilder.GeodeticPoint[])clippedVertices.Clone();
        Array.Reverse(reversed);
        return reversed;
    }

    private static double ComputeSignedArea(LocalCityGmlResonitePlanBuilder.GeodeticPoint[] vertices)
    {
        if (vertices.Length < 3)
        {
            return 0.0;
        }

        double signedArea = 0.0;
        for (int index = 0; index < vertices.Length; index++)
        {
            LocalCityGmlResonitePlanBuilder.GeodeticPoint current = vertices[index];
            LocalCityGmlResonitePlanBuilder.GeodeticPoint next = vertices[(index + 1) % vertices.Length];
            signedArea += (current.Longitude * next.Latitude) - (next.Longitude * current.Latitude);
        }

        return signedArea * 0.5;
    }

    private static bool TryResolveTrianglePoint(
        LocalCityGmlResonitePlanBuilder.GeodeticPoint a,
        LocalCityGmlResonitePlanBuilder.GeodeticPoint b,
        LocalCityGmlResonitePlanBuilder.GeodeticPoint c,
        Coordinate coordinate,
        out LocalCityGmlResonitePlanBuilder.GeodeticPoint point)
    {
        double denominator =
            ((b.Latitude - c.Latitude) * (a.Longitude - c.Longitude))
            + ((c.Longitude - b.Longitude) * (a.Latitude - c.Latitude));
        if (Math.Abs(denominator) <= 1e-18)
        {
            point = null!;
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
            point = null!;
            return false;
        }

        double altitude = (a.Altitude * weightA) + (b.Altitude * weightB) + (c.Altitude * weightC);
        point = new LocalCityGmlResonitePlanBuilder.GeodeticPoint(coordinate.Y, coordinate.X, altitude);
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
