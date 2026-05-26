using System;
using System.Collections.Generic;
using System.Linq;

using GeographicLib;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class GeneratedLod1RoofCityObjectFactory
{
    private const double BuildingBottomCullBandMeters = 0.1;

    internal static ParsedCityObject Create(ParsedCityObject cityObject)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        if (!PlateauPackageCatalog.IsBuildingPackage(cityObject.PackageName)
            || cityObject.LodLevel != 1
            || !cityObject.ReferenceSystem.IsGeographic
            || cityObject.Surfaces.Any(GeneratedLod1RoofSurfaceIdentity.IsGenerated))
        {
            return cityObject;
        }

        GeodeticPoint cityObjectOrigin = CityObjectOriginResolver.Resolve(
            cityObject.GeodeticOriginOverride,
            cityObject.Surfaces.SelectMany(static surface => surface.Vertices));
        LocalCartesian cityObjectCartesian = new(
            cityObjectOrigin.Latitude,
            cityObjectOrigin.Longitude,
            cityObjectOrigin.Altitude,
            cityObject.ReferenceSystem.Geocentric);
        if (!TryCreateFootprint(cityObject, cityObjectOrigin, cityObjectCartesian, out Lod1RoofFootprint? footprint))
        {
            return cityObject;
        }

        Lod1RoofFootprint resolvedFootprint = footprint!;
        GeneratedLod1RoofShape roofShape = Lod1RoofShapePolicy.Select(
            cityObject.SlotKey,
            resolvedFootprint.Attributes,
            resolvedFootprint.GeometryHeightMeters,
            resolvedFootprint.LengthMeters,
            resolvedFootprint.WidthMeters);
        if (roofShape == GeneratedLod1RoofShape.Flat)
        {
            return cityObject;
        }

        ParsedSurface[] generatedSurfaces = GeneratedLod1RoofSurfaceFactory.Create(resolvedFootprint, roofShape);
        if (generatedSurfaces.Length == 0)
        {
            return cityObject;
        }

        ParsedSurface[] surfaces =
        [
            .. cityObject.Surfaces.Where(surface => !string.Equals(surface.PolygonId, resolvedFootprint.TopSurface.PolygonId, StringComparison.Ordinal)),
            .. generatedSurfaces,
        ];
        return cityObject with { Surfaces = surfaces };
    }

    private static bool TryCreateFootprint(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian cityObjectCartesian,
        out Lod1RoofFootprint? footprint)
    {
        footprint = null;
        SurfaceProjectionInfo[] surfaceInfos = cityObject.Surfaces
            .Select(surface => CreateSurfaceProjectionInfo(surface, cityObjectOrigin, cityObjectCartesian))
            .Where(static info => info.MinimumY.HasValue && info.MaximumY.HasValue)
            .ToArray();
        if (surfaceInfos.Length == 0)
        {
            return false;
        }

        double objectMinimumY = surfaceInfos.Min(static info => info.MinimumY!.Value);
        double objectMaximumY = surfaceInfos.Max(static info => info.MaximumY!.Value);
        double geometryHeight = objectMaximumY - objectMinimumY;
        SurfaceProjectionInfo[] topCandidates = surfaceInfos
            .Where(static info => info.IsNearHorizontal)
            .Where(info => info.MaximumY!.Value >= objectMaximumY - 0.1)
            .Where(info => info.MinimumY!.Value > objectMinimumY + BuildingBottomCullBandMeters)
            .ToArray();
        if (topCandidates.Length != 1)
        {
            return false;
        }

        ParsedSurface topSurface = topCandidates[0].Surface;
        if (topSurface.TexturePayload is not null || topSurface.InteriorRings.Length != 0)
        {
            return false;
        }

        GeodeticPoint[] ring = RemoveClosingPoint(topSurface.ExteriorRing.Vertices);
        if (ring.Length != 4)
        {
            return false;
        }

        Float3[] positions = ring
            .Select(point => CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        if (!TryClassifyRectangle(positions, out bool firstEdgeIsLongAxis, out double length, out double width))
        {
            return false;
        }

        footprint = new Lod1RoofFootprint(
            topSurface,
            ring,
            length,
            width,
            geometryHeight,
            cityObject.BuildingAttributes ?? BuildingAttributeContext.Empty,
            firstEdgeIsLongAxis);
        return true;
    }

    private static SurfaceProjectionInfo CreateSurfaceProjectionInfo(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian cityObjectCartesian)
    {
        Float3[] positions = surface.Vertices
            .Select(point => CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        if (positions.Length == 0)
        {
            return new SurfaceProjectionInfo(surface, null, null, false);
        }

        Float3? normal = ComputePolygonNormal(positions);
        bool isNearHorizontal = normal is not null && Math.Abs(normal.Y) >= 0.98;

        return new SurfaceProjectionInfo(
            surface,
            positions.Min(static position => position.Y),
            positions.Max(static position => position.Y),
            isNearHorizontal);
    }

    private static GeodeticPoint[] RemoveClosingPoint(GeodeticPoint[] vertices)
    {
        if (vertices.Length > 1 && AreSamePoint(vertices[0], vertices[^1]))
        {
            return vertices.Take(vertices.Length - 1).ToArray();
        }

        return vertices.ToArray();
    }

    private static bool TryClassifyRectangle(
        Float3[] positions,
        out bool firstEdgeIsLongAxis,
        out double length,
        out double width)
    {
        firstEdgeIsLongAxis = false;
        length = 0.0;
        width = 0.0;
        if (positions.Length != 4)
        {
            return false;
        }

        double[] edges =
        [
            HorizontalDistance(positions[0], positions[1]),
            HorizontalDistance(positions[1], positions[2]),
            HorizontalDistance(positions[2], positions[3]),
            HorizontalDistance(positions[3], positions[0]),
        ];
        if (edges.Any(static edge => edge < 1.0))
        {
            return false;
        }

        if (!ApproximatelyEqual(edges[0], edges[2], 0.15)
            || !ApproximatelyEqual(edges[1], edges[3], 0.15))
        {
            return false;
        }

        Float3 edge0 = NormalizeHorizontal(Subtract(positions[1], positions[0]));
        Float3 edge1 = NormalizeHorizontal(Subtract(positions[2], positions[1]));
        if (Math.Abs(Dot(edge0, edge1)) > 0.15)
        {
            return false;
        }

        firstEdgeIsLongAxis = edges[0] >= edges[1];
        length = Math.Max(edges[0], edges[1]);
        width = Math.Min(edges[0], edges[1]);
        return true;
    }

    private static Float3? ComputePolygonNormal(IEnumerable<Float3> positions)
    {
        Float3[] points = positions.ToArray();
        if (points.Length < 3)
        {
            return null;
        }

        double normalX = 0.0;
        double normalY = 0.0;
        double normalZ = 0.0;

        for (int index = 0; index < points.Length; index++)
        {
            Float3 current = points[index];
            Float3 next = points[(index + 1) % points.Length];
            normalX += (current.Y - next.Y) * (current.Z + next.Z);
            normalY += (current.Z - next.Z) * (current.X + next.X);
            normalZ += (current.X - next.X) * (current.Y + next.Y);
        }

        double magnitude = Math.Sqrt((normalX * normalX) + (normalY * normalY) + (normalZ * normalZ));
        if (magnitude < 1e-8)
        {
            return null;
        }

        return new Float3(normalX / magnitude, normalY / magnitude, normalZ / magnitude);
    }

    private static Float3 CreateScenePosition(
        GeodeticPoint point,
        GeodeticPoint origin,
        LocalCartesian cartesian)
    {
        return SceneAxisMapper.CreatePosition(
            point.Latitude,
            point.Longitude,
            point.Altitude,
            origin.Latitude,
            origin.Longitude,
            origin.Altitude,
            cartesian);
    }

    private static bool AreSamePoint(GeodeticPoint left, GeodeticPoint right)
    {
        const double tolerance = 1e-8;
        return Math.Abs(left.Latitude - right.Latitude) < tolerance
            && Math.Abs(left.Longitude - right.Longitude) < tolerance
            && Math.Abs(left.Altitude - right.Altitude) < tolerance;
    }

    private static Float3 NormalizeHorizontal(Float3 value)
    {
        Float3 horizontal = new(value.X, 0.0, value.Z);
        double lengthSquared = (horizontal.X * horizontal.X) + (horizontal.Z * horizontal.Z);
        if (lengthSquared < 1e-12)
        {
            return horizontal;
        }

        double length = Math.Sqrt(lengthSquared);
        return new Float3(horizontal.X / length, 0.0, horizontal.Z / length);
    }

    private static double HorizontalDistance(Float3 left, Float3 right)
    {
        double deltaX = left.X - right.X;
        double deltaZ = left.Z - right.Z;
        return Math.Sqrt((deltaX * deltaX) + (deltaZ * deltaZ));
    }

    private static bool ApproximatelyEqual(double left, double right, double relativeTolerance)
    {
        double scale = Math.Max(Math.Max(Math.Abs(left), Math.Abs(right)), 1.0);
        return Math.Abs(left - right) <= scale * relativeTolerance;
    }

    private static Float3 Subtract(Float3 left, Float3 right)
    {
        return new Float3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    }

    private static double Dot(Float3 left, Float3 right)
    {
        return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
    }

    private readonly record struct SurfaceProjectionInfo(
        ParsedSurface Surface,
        double? MinimumY,
        double? MaximumY,
        bool IsNearHorizontal);
}
