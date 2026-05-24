using System;
using System.Collections.Generic;
using System.Linq;

using GeographicLib;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class Lod1RoofGenerator
{
    private const double BuildingBottomCullBandMeters = 0.1;

    public static ParsedCityObject Apply(ParsedCityObject cityObject)
    {
        ArgumentNullException.ThrowIfNull(cityObject);

        if (!PlateauPackageCatalog.IsBuildingPackage(cityObject.PackageName)
            || cityObject.LodLevel != 1
            || !cityObject.ReferenceSystem.IsGeographic
            || cityObject.Surfaces.Any(static surface => IsGeneratedSurface(surface)))
        {
            return cityObject;
        }

        GeodeticPoint cityObjectOrigin = CityObjectGeometryMetrics.GetCenterOrigin(cityObject);
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
        Lod1RoofShape roofShape = Lod1RoofShapePolicy.Select(
            cityObject.SlotKey,
            resolvedFootprint.Attributes,
            resolvedFootprint.LengthMeters,
            resolvedFootprint.WidthMeters,
            resolvedFootprint.GeometryHeightMeters);
        if (roofShape == Lod1RoofShape.Flat)
        {
            return cityObject;
        }

        ParsedSurface[] generatedSurfaces = CreateGeneratedSurfaces(resolvedFootprint, roofShape);
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

    public static bool IsGeneratedSurface(ParsedSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return IsGeneratedSurfaceId(surface.PolygonId);
    }

    private static bool IsGeneratedSurfaceId(string polygonId)
    {
        return polygonId.Contains("_generated_shed-", StringComparison.Ordinal)
            || polygonId.Contains("_generated_gable-", StringComparison.Ordinal)
            || polygonId.Contains("_generated_hip-", StringComparison.Ordinal);
    }

    private static bool TryCreateFootprint(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian cityObjectCartesian,
        out Lod1RoofFootprint? footprint)
    {
        footprint = null;
        SurfaceProjectionSnapshot[] surfaceInfos = cityObject.Surfaces
            .Select(surface => SurfaceProjectionSnapshotFactory.Create(surface, cityObjectOrigin, cityObjectCartesian))
            .Where(static info => info.MinimumY.HasValue && info.MaximumY.HasValue)
            .ToArray();
        if (surfaceInfos.Length == 0)
        {
            return false;
        }

        double objectMinimumY = surfaceInfos.Min(static info => info.MinimumY!.Value);
        double objectMaximumY = surfaceInfos.Max(static info => info.MaximumY!.Value);
        double geometryHeight = objectMaximumY - objectMinimumY;
        SurfaceProjectionSnapshot[] topCandidates = surfaceInfos
            .Where(static info => info.IsNearHorizontal)
            .Where(info => info.MaximumY!.Value >= objectMaximumY - 0.1)
            .Where(info => info.MinimumY!.Value > objectMinimumY + BuildingBottomCullBandMeters)
            .ToArray();
        if (topCandidates.Length != 1)
        {
            return false;
        }

        ParsedSurface topSurface = topCandidates[0].Surface;
        if (topSurface.TexturePayload is not null
            || topSurface.InteriorRings.Length != 0)
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

    private static ParsedSurface[] CreateGeneratedSurfaces(
        Lod1RoofFootprint footprint,
        Lod1RoofShape shape)
    {
        double rise = ComputeRiseMeters(footprint);
        return shape switch
        {
            Lod1RoofShape.Shed => CreateShedRoofSurfaces(footprint, rise),
            Lod1RoofShape.Gable => CreateGableRoofSurfaces(footprint, rise),
            Lod1RoofShape.Hip => CreateHipRoofSurfaces(footprint, rise),
            _ => [],
        };
    }

    private static double ComputeRiseMeters(Lod1RoofFootprint footprint)
    {
        double contextualLimit = Math.Max(0.6, footprint.GeometryHeightMeters * 0.18);
        return Math.Clamp(Math.Min(footprint.WidthMeters * 0.28, contextualLimit), 0.4, 2.2);
    }

    private static ParsedSurface[] CreateShedRoofSurfaces(
        Lod1RoofFootprint footprint,
        double rise)
    {
        GeodeticPoint[] c = footprint.Corners;
        bool firstLong = footprint.FirstEdgeIsLongAxis;
        GeodeticPoint[] highEdge = firstLong ? [Elevate(c[2], rise), Elevate(c[3], rise)] : [Elevate(c[1], rise), Elevate(c[2], rise)];
        GeodeticPoint[] roof = firstLong
            ? [c[0], c[1], highEdge[0], highEdge[1]]
            : [c[0], highEdge[0], highEdge[1], c[3]];

        List<ParsedSurface> surfaces =
        [
            CreateGeneratedSurface(footprint, "shed-roof", ParsedSurfaceSemantic.Roof, roof),
        ];
        if (firstLong)
        {
            surfaces.Add(CreateGeneratedSurface(footprint, "shed-high-wall", ParsedSurfaceSemantic.Wall, [c[3], c[2], highEdge[0], highEdge[1]]));
            surfaces.Add(CreateGeneratedSurface(footprint, "shed-side-wall-a", ParsedSurfaceSemantic.Wall, [c[1], c[2], highEdge[0]]));
            surfaces.Add(CreateGeneratedSurface(footprint, "shed-side-wall-b", ParsedSurfaceSemantic.Wall, [c[0], highEdge[1], c[3]]));
        }
        else
        {
            surfaces.Add(CreateGeneratedSurface(footprint, "shed-high-wall", ParsedSurfaceSemantic.Wall, [c[1], c[2], highEdge[1], highEdge[0]]));
            surfaces.Add(CreateGeneratedSurface(footprint, "shed-side-wall-a", ParsedSurfaceSemantic.Wall, [c[0], c[1], highEdge[0]]));
            surfaces.Add(CreateGeneratedSurface(footprint, "shed-side-wall-b", ParsedSurfaceSemantic.Wall, [c[3], highEdge[1], c[2]]));
        }

        return surfaces.ToArray();
    }

    private static ParsedSurface[] CreateGableRoofSurfaces(
        Lod1RoofFootprint footprint,
        double rise)
    {
        GeodeticPoint[] c = footprint.Corners;
        bool firstLong = footprint.FirstEdgeIsLongAxis;
        GeodeticPoint ridge0;
        GeodeticPoint ridge1;
        if (firstLong)
        {
            ridge0 = Elevate(Lerp(c[0], c[3], 0.5), rise);
            ridge1 = Elevate(Lerp(c[1], c[2], 0.5), rise);
            return
            [
                CreateGeneratedSurface(footprint, "gable-roof-a", ParsedSurfaceSemantic.Roof, [c[0], c[1], ridge1, ridge0]),
                CreateGeneratedSurface(footprint, "gable-roof-b", ParsedSurfaceSemantic.Roof, [c[3], ridge0, ridge1, c[2]]),
                CreateGeneratedSurface(footprint, "gable-wall-a", ParsedSurfaceSemantic.Wall, [c[0], ridge0, c[3]]),
                CreateGeneratedSurface(footprint, "gable-wall-b", ParsedSurfaceSemantic.Wall, [c[1], c[2], ridge1]),
            ];
        }

        ridge0 = Elevate(Lerp(c[0], c[1], 0.5), rise);
        ridge1 = Elevate(Lerp(c[3], c[2], 0.5), rise);
        return
        [
            CreateGeneratedSurface(footprint, "gable-roof-a", ParsedSurfaceSemantic.Roof, [c[0], ridge0, ridge1, c[3]]),
            CreateGeneratedSurface(footprint, "gable-roof-b", ParsedSurfaceSemantic.Roof, [ridge0, c[1], c[2], ridge1]),
            CreateGeneratedSurface(footprint, "gable-wall-a", ParsedSurfaceSemantic.Wall, [c[0], c[1], ridge0]),
            CreateGeneratedSurface(footprint, "gable-wall-b", ParsedSurfaceSemantic.Wall, [c[3], ridge1, c[2]]),
        ];
    }

    private static ParsedSurface[] CreateHipRoofSurfaces(
        Lod1RoofFootprint footprint,
        double rise)
    {
        GeodeticPoint[] c = footprint.Corners;
        if (footprint.FirstEdgeIsLongAxis)
        {
            GeodeticPoint leftMid = Lerp(c[0], c[3], 0.5);
            GeodeticPoint rightMid = Lerp(c[1], c[2], 0.5);
            GeodeticPoint longRidge0 = Elevate(Lerp(leftMid, rightMid, 0.25), rise);
            GeodeticPoint longRidge1 = Elevate(Lerp(leftMid, rightMid, 0.75), rise);
            return
            [
                CreateGeneratedSurface(footprint, "hip-roof-a", ParsedSurfaceSemantic.Roof, [c[0], c[1], longRidge1, longRidge0]),
                CreateGeneratedSurface(footprint, "hip-roof-b", ParsedSurfaceSemantic.Roof, [c[3], longRidge0, longRidge1, c[2]]),
                CreateGeneratedSurface(footprint, "hip-roof-c", ParsedSurfaceSemantic.Roof, [c[0], longRidge0, c[3]]),
                CreateGeneratedSurface(footprint, "hip-roof-d", ParsedSurfaceSemantic.Roof, [c[1], c[2], longRidge1]),
            ];
        }

        GeodeticPoint bottomMid = Lerp(c[0], c[1], 0.5);
        GeodeticPoint topMid = Lerp(c[3], c[2], 0.5);
        GeodeticPoint shortRidge0 = Elevate(Lerp(bottomMid, topMid, 0.25), rise);
        GeodeticPoint shortRidge1 = Elevate(Lerp(bottomMid, topMid, 0.75), rise);
        return
        [
            CreateGeneratedSurface(footprint, "hip-roof-a", ParsedSurfaceSemantic.Roof, [c[0], shortRidge0, shortRidge1, c[3]]),
            CreateGeneratedSurface(footprint, "hip-roof-b", ParsedSurfaceSemantic.Roof, [shortRidge0, c[1], c[2], shortRidge1]),
            CreateGeneratedSurface(footprint, "hip-roof-c", ParsedSurfaceSemantic.Roof, [c[0], c[1], shortRidge0]),
            CreateGeneratedSurface(footprint, "hip-roof-d", ParsedSurfaceSemantic.Roof, [c[3], shortRidge1, c[2]]),
        ];
    }

    private static ParsedSurface CreateGeneratedSurface(
        Lod1RoofFootprint footprint,
        string suffix,
        ParsedSurfaceSemantic semantic,
        GeodeticPoint[] vertices)
    {
        string polygonId = $"{footprint.TopSurface.PolygonId}_generated_{suffix}";
        GeodeticPoint[] orientedVertices =
            semantic == ParsedSurfaceSemantic.Wall
                ? OrientWallVerticesForOutwardMeshFaces(footprint, vertices)
                : semantic == ParsedSurfaceSemantic.Roof
                ? OrientRoofVerticesForUpwardMeshFaces(footprint, vertices)
                : vertices;
        GeodeticPoint[] closedVertices = [.. orientedVertices, orientedVertices[0]];
        return new ParsedSurface(
            polygonId,
            semantic,
            new ParsedRing(
                $"{polygonId}-ring",
                closedVertices,
                UVs: null),
            InteriorRings: [],
            footprint.TopSurface.BaseColor,
            TexturePayload: null,
            UsesGeneratedDemTexture: false,
            footprint.TopSurface.OpticalProperties);
    }

    private static GeodeticPoint[] OrientWallVerticesForOutwardMeshFaces(
        Lod1RoofFootprint footprint,
        GeodeticPoint[] vertices)
    {
        if (vertices.Length < 3)
        {
            return vertices;
        }

        double referenceLatitude = footprint.Corners.Average(static point => point.Latitude);
        double referenceLongitude = footprint.Corners.Average(static point => point.Longitude);
        Float3[] footprintPositions = footprint.Corners
            .Select(point => CreateApproximateHorizontalPosition(point, referenceLatitude, referenceLongitude))
            .ToArray();
        Float3[] wallPositions = vertices
            .Select(point => CreateApproximateHorizontalPosition(point, referenceLatitude, referenceLongitude))
            .ToArray();
        Float3? normal = SurfaceGeometryMath.ComputeNewellNormal(wallPositions);
        if (normal is null)
        {
            return vertices;
        }

        Float3 footprintCenter = AveragePosition(footprintPositions);
        Float3 wallCenter = AveragePosition(wallPositions);
        Float3 outwardDirection = new(wallCenter.X - footprintCenter.X, 0.0, wallCenter.Z - footprintCenter.Z);
        Float3 horizontalNormal = new(normal.X, 0.0, normal.Z);
        if (Dot(horizontalNormal, outwardDirection) <= 0.0)
        {
            return vertices;
        }

        GeodeticPoint[] reversed = vertices.ToArray();
        Array.Reverse(reversed);
        return reversed;
    }

    private static GeodeticPoint[] OrientRoofVerticesForUpwardMeshFaces(
        Lod1RoofFootprint footprint,
        GeodeticPoint[] vertices)
    {
        if (vertices.Length < 3)
        {
            return vertices;
        }

        double referenceLatitude = footprint.Corners.Average(static point => point.Latitude);
        double referenceLongitude = footprint.Corners.Average(static point => point.Longitude);
        Float3[] roofPositions = vertices
            .Select(point => CreateApproximateHorizontalPosition(point, referenceLatitude, referenceLongitude))
            .ToArray();
        Float3? normal = SurfaceGeometryMath.ComputeNewellNormal(roofPositions);
        if (normal is null)
        {
            return vertices;
        }

        if (normal.Y <= 0.0)
        {
            return vertices;
        }

        GeodeticPoint[] reversed = vertices.ToArray();
        Array.Reverse(reversed);
        return reversed;
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

    private static Float3 CreateApproximateHorizontalPosition(
        GeodeticPoint point,
        double referenceLatitude,
        double referenceLongitude)
    {
        const double metersPerLatitudeDegree = 111_320.0;
        double metersPerLongitudeDegree = metersPerLatitudeDegree * Math.Cos(referenceLatitude * (Math.PI / 180.0));
        return new Float3(
            (point.Longitude - referenceLongitude) * metersPerLongitudeDegree,
            point.Altitude,
            (point.Latitude - referenceLatitude) * metersPerLatitudeDegree);
    }

    private static Float3 AveragePosition(IReadOnlyList<Float3> positions)
    {
        return new Float3(
            positions.Average(static position => position.X),
            positions.Average(static position => position.Y),
            positions.Average(static position => position.Z));
    }

    private static GeodeticPoint Elevate(GeodeticPoint point, double rise)
    {
        return point with { Altitude = point.Altitude + rise };
    }

    private static GeodeticPoint Lerp(GeodeticPoint source, GeodeticPoint target, double ratio)
    {
        return new GeodeticPoint(
            source.Latitude + ((target.Latitude - source.Latitude) * ratio),
            source.Longitude + ((target.Longitude - source.Longitude) * ratio),
            source.Altitude + ((target.Altitude - source.Altitude) * ratio));
    }

    private static bool AreSamePoint(GeodeticPoint left, GeodeticPoint right)
    {
        return Math.Abs(left.Latitude - right.Latitude) < 1e-8
            && Math.Abs(left.Longitude - right.Longitude) < 1e-8
            && Math.Abs(left.Altitude - right.Altitude) < 1e-8;
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

    private static Float3 NormalizeHorizontal(Float3 value)
    {
        double length = Math.Sqrt((value.X * value.X) + (value.Z * value.Z));
        if (length < 1e-8)
        {
            return new Float3(0.0, 0.0, 0.0);
        }

        return new Float3(value.X / length, 0.0, value.Z / length);
    }

    private static Float3 Subtract(Float3 left, Float3 right)
    {
        return new Float3(
            left.X - right.X,
            left.Y - right.Y,
            left.Z - right.Z);
    }

    private static double Dot(Float3 left, Float3 right)
    {
        return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
    }

    private sealed record Lod1RoofFootprint(
        ParsedSurface TopSurface,
        GeodeticPoint[] Corners,
        double LengthMeters,
        double WidthMeters,
        double GeometryHeightMeters,
        BuildingAttributeContext Attributes,
        bool FirstEdgeIsLongAxis);

}
