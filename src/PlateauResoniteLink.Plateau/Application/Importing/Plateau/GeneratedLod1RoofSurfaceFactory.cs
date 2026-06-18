using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Core.Application.Importing.Contracts;
using PlateauResoniteLink.Plateau.Application.Importing.Source;

namespace PlateauResoniteLink.Plateau.Application.Importing.Plateau;

internal static class GeneratedLod1RoofSurfaceFactory
{
    internal static ParsedSurface[] Create(
        Lod1RoofFootprint footprint,
        GeneratedLod1RoofShape shape)
    {
        ArgumentNullException.ThrowIfNull(footprint);

        double rise = ComputeGeneratedRoofRiseMeters(footprint);
        return shape switch
        {
            GeneratedLod1RoofShape.Shed => CreateShedRoofSurfaces(footprint, rise),
            GeneratedLod1RoofShape.Gable => CreateGableRoofSurfaces(footprint, rise),
            GeneratedLod1RoofShape.Hip => CreateHipRoofSurfaces(footprint, rise),
            _ => [],
        };
    }

    private static double ComputeGeneratedRoofRiseMeters(Lod1RoofFootprint footprint)
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
            CreateGeneratedRoofSurface(footprint, ParsedSurfaceSemantic.Roof, roof),
        ];
        if (firstLong)
        {
            surfaces.Add(CreateGeneratedRoofSurface(footprint, ParsedSurfaceSemantic.Wall, [c[3], c[2], highEdge[0], highEdge[1]]));
            surfaces.Add(CreateGeneratedRoofSurface(footprint, ParsedSurfaceSemantic.Wall, [c[1], c[2], highEdge[0]]));
            surfaces.Add(CreateGeneratedRoofSurface(footprint, ParsedSurfaceSemantic.Wall, [c[0], highEdge[1], c[3]]));
        }
        else
        {
            surfaces.Add(CreateGeneratedRoofSurface(footprint, ParsedSurfaceSemantic.Wall, [c[1], c[2], highEdge[1], highEdge[0]]));
            surfaces.Add(CreateGeneratedRoofSurface(footprint, ParsedSurfaceSemantic.Wall, [c[0], c[1], highEdge[0]]));
            surfaces.Add(CreateGeneratedRoofSurface(footprint, ParsedSurfaceSemantic.Wall, [c[3], highEdge[1], c[2]]));
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
                CreateGeneratedRoofSurface(footprint, ParsedSurfaceSemantic.Roof, [c[0], c[1], ridge1, ridge0]),
                CreateGeneratedRoofSurface(footprint, ParsedSurfaceSemantic.Roof, [c[3], ridge0, ridge1, c[2]]),
                CreateGeneratedRoofSurface(footprint, ParsedSurfaceSemantic.Wall, [c[0], ridge0, c[3]]),
                CreateGeneratedRoofSurface(footprint, ParsedSurfaceSemantic.Wall, [c[1], c[2], ridge1]),
            ];
        }

        ridge0 = Elevate(Lerp(c[0], c[1], 0.5), rise);
        ridge1 = Elevate(Lerp(c[3], c[2], 0.5), rise);
        return
        [
            CreateGeneratedRoofSurface(footprint, ParsedSurfaceSemantic.Roof, [c[0], ridge0, ridge1, c[3]]),
            CreateGeneratedRoofSurface(footprint, ParsedSurfaceSemantic.Roof, [ridge0, c[1], c[2], ridge1]),
            CreateGeneratedRoofSurface(footprint, ParsedSurfaceSemantic.Wall, [c[0], c[1], ridge0]),
            CreateGeneratedRoofSurface(footprint, ParsedSurfaceSemantic.Wall, [c[3], ridge1, c[2]]),
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
                CreateGeneratedRoofSurface(footprint, ParsedSurfaceSemantic.Roof, [c[0], c[1], longRidge1, longRidge0]),
                CreateGeneratedRoofSurface(footprint, ParsedSurfaceSemantic.Roof, [c[3], longRidge0, longRidge1, c[2]]),
                CreateGeneratedRoofSurface(footprint, ParsedSurfaceSemantic.Roof, [c[0], longRidge0, c[3]]),
                CreateGeneratedRoofSurface(footprint, ParsedSurfaceSemantic.Roof, [c[1], c[2], longRidge1]),
            ];
        }

        GeodeticPoint bottomMid = Lerp(c[0], c[1], 0.5);
        GeodeticPoint topMid = Lerp(c[3], c[2], 0.5);
        GeodeticPoint shortRidge0 = Elevate(Lerp(bottomMid, topMid, 0.25), rise);
        GeodeticPoint shortRidge1 = Elevate(Lerp(bottomMid, topMid, 0.75), rise);
        return
        [
            CreateGeneratedRoofSurface(footprint, ParsedSurfaceSemantic.Roof, [c[0], shortRidge0, shortRidge1, c[3]]),
            CreateGeneratedRoofSurface(footprint, ParsedSurfaceSemantic.Roof, [shortRidge0, c[1], c[2], shortRidge1]),
            CreateGeneratedRoofSurface(footprint, ParsedSurfaceSemantic.Roof, [c[0], c[1], shortRidge0]),
            CreateGeneratedRoofSurface(footprint, ParsedSurfaceSemantic.Roof, [c[3], shortRidge1, c[2]]),
        ];
    }

    private static ParsedSurface CreateGeneratedRoofSurface(
        Lod1RoofFootprint footprint,
        ParsedSurfaceSemantic semantic,
        GeodeticPoint[] vertices)
    {
        GeodeticPoint[] orientedVertices =
            semantic == ParsedSurfaceSemantic.Wall
                ? OrientGeneratedWallVerticesForOutwardMeshFaces(footprint, vertices)
                : semantic == ParsedSurfaceSemantic.Roof
                ? OrientGeneratedRoofVerticesForUpwardMeshFaces(footprint, vertices)
                : vertices;
        GeodeticPoint[] closedVertices = [.. orientedVertices, orientedVertices[0]];
        return new ParsedSurface(
            semantic,
            new ParsedRing(
                closedVertices,
                UVs: null),
            InteriorRings: [],
            footprint.TopSurface.BaseColor,
            TexturePayload: null,
            footprint.TopSurface.OpticalProperties);
    }

    private static GeodeticPoint[] OrientGeneratedWallVerticesForOutwardMeshFaces(
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
        Float3? normal = PolygonNormal.Compute(wallPositions);
        if (normal is null)
        {
            return vertices;
        }

        Float3 footprintCenter = AveragePosition(footprintPositions);
        Float3 wallCenter = AveragePosition(wallPositions);
        Float3 outwardDirection = new(wallCenter.X - footprintCenter.X, 0.0, wallCenter.Z - footprintCenter.Z);
        Float3 horizontalNormal = new(normal.X, 0.0, normal.Z);
        // Surface tessellation emits Resonite triangle winding opposite to the parsed polygon normal.
        // Generated walls therefore need an inward parsed normal so the emitted mesh face points outward.
        if (Dot(horizontalNormal, outwardDirection) <= 0.0)
        {
            return vertices;
        }

        GeodeticPoint[] reversed = vertices.ToArray();
        Array.Reverse(reversed);
        return reversed;
    }

    private static GeodeticPoint[] OrientGeneratedRoofVerticesForUpwardMeshFaces(
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
        Float3? normal = PolygonNormal.Compute(roofPositions);
        if (normal is null)
        {
            return vertices;
        }

        // Surface tessellation emits Resonite triangle winding opposite to the parsed polygon normal.
        // Generated roofs therefore need a downward parsed normal so the emitted mesh face points upward.
        if (normal.Y <= 0.0)
        {
            return vertices;
        }

        GeodeticPoint[] reversed = vertices.ToArray();
        Array.Reverse(reversed);
        return reversed;
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

    private static GeodeticPoint Lerp(
        GeodeticPoint source,
        GeodeticPoint target,
        double ratio)
    {
        return new GeodeticPoint(
            source.Latitude + ((target.Latitude - source.Latitude) * ratio),
            source.Longitude + ((target.Longitude - source.Longitude) * ratio),
            source.Altitude + ((target.Altitude - source.Altitude) * ratio));
    }

    private static double Dot(Float3 left, Float3 right)
    {
        return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
    }
}

internal sealed record Lod1RoofFootprint(
    ParsedSurface TopSurface,
    GeodeticPoint[] Corners,
    double LengthMeters,
    double WidthMeters,
    double GeometryHeightMeters,
    BuildingAttributeContext Attributes,
    bool FirstEdgeIsLongAxis);
