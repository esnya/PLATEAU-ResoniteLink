using System;
using System.Collections.Generic;
using System.Linq;

using GeographicLib;

namespace PlateauResoniteLink.Application.Importing;

internal static class GeneratedRoadMarkingCityObjectFactory
{
    internal const double DefaultMarkingWidthMeters = 0.15;
    internal const double DefaultSegmentLengthMeters = 5.0;

    private static readonly ColorRgba DefaultMarkingColor = new(1.0, 1.0, 1.0, 1.0);

    internal static ParsedCityObject? Create(
        ParsedCityObject cityObject,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        if (!string.Equals(cityObject.PackageName, "tran", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        List<ParsedSurface> markingSurfaces = [];
        foreach (ParsedSurface surface in cityObject.Surfaces)
        {
            if (surface.TexturePayload is not null)
            {
                continue;
            }

            List<ParsedSurface> generatedSurfaces = CreateSurfaces(
                surface,
                cityObjectOrigin,
                cityObjectCartesian);
            if (generatedSurfaces.Count == 0)
            {
                continue;
            }

            markingSurfaces.AddRange(generatedSurfaces);
        }

        return markingSurfaces.Count == 0
            ? null
            : cityObject with
            {
                SlotKey = $"{cityObject.SlotKey}_road_marking",
                DisplayName = $"{cityObject.DisplayName} Marking",
                Surfaces = markingSurfaces.ToArray(),
            };
    }

    private static List<ParsedSurface> CreateSurfaces(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        GeodeticPoint[] vertices = surface.ExteriorRing.Vertices;
        if (vertices.Length != 4 || surface.InteriorRings.Length != 0)
        {
            return [];
        }

        Float3[] positions = vertices
            .Select(point => CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        Float3? normal = ComputePolygonNormal(positions);
        if (normal is null || Math.Abs(normal.Y) < 0.7)
        {
            return [];
        }

        RoadMarkingEdgePair edgePair = SelectEdgePair(vertices, positions);
        if (edgePair.Length < 1.0 || edgePair.Width < 0.3)
        {
            return [];
        }

        double markingWidth = Math.Min(DefaultMarkingWidthMeters, edgePair.Width * 0.5);
        double insetDistance = Math.Max((edgePair.Width - markingWidth) * 0.5, 0.0);
        if (insetDistance <= 1e-6)
        {
            return [];
        }

        int segmentCount = Math.Max(
            1,
            (int)Math.Ceiling(edgePair.Length / DefaultSegmentLengthMeters));
        List<ParsedSurface> segments = new(segmentCount);

        for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            double startT = (double)segmentIndex / segmentCount;
            double endT = (double)(segmentIndex + 1) / segmentCount;
            GeodeticPoint side0Start = InterpolateAlongEdge(edgePair.Side0[0], edgePair.Side0[1], startT);
            GeodeticPoint side0End = InterpolateAlongEdge(edgePair.Side0[0], edgePair.Side0[1], endT);
            GeodeticPoint side1Start = InterpolateAlongEdge(edgePair.Side1[0], edgePair.Side1[1], startT);
            GeodeticPoint side1End = InterpolateAlongEdge(edgePair.Side1[0], edgePair.Side1[1], endT);

            GeodeticPoint[] side0Source = [side0Start, side0End];
            GeodeticPoint[] side1Source = [side1Start, side1End];
            Float3[] side0Positions = side0Source
                .Select(point => CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian))
                .ToArray();
            Float3[] side1Positions = side1Source
                .Select(point => CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian))
                .ToArray();

            GeodeticPoint[] side0 = MoveTowardCrossSection(
                side0Source,
                side1Source,
                side0Positions,
                side1Positions,
                insetDistance);
            GeodeticPoint[] side1 = MoveTowardCrossSection(
                side1Source,
                side0Source,
                side1Positions,
                side0Positions,
                insetDistance);
            segments.Add(new ParsedSurface(
                $"{surface.PolygonId}_generated_marking_{segmentIndex:D2}",
                surface.Semantic,
                new ParsedRing(
                    $"{surface.ExteriorRing.RingId}_generated_marking_{segmentIndex:D2}",
                    [side0[0], side0[1], side1[1], side1[0]],
                    UVs: null),
                [],
                DefaultMarkingColor,
                TexturePayload: null));
        }

        return segments;
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

    private static RoadMarkingEdgePair SelectEdgePair(
        GeodeticPoint[] vertices,
        Float3[] positions)
    {
        double edge01 = Distance(positions[0], positions[1]);
        double edge12 = Distance(positions[1], positions[2]);
        double edge23 = Distance(positions[2], positions[3]);
        double edge30 = Distance(positions[3], positions[0]);

        double pair01Length = (edge01 + edge23) * 0.5;
        double pair12Length = (edge12 + edge30) * 0.5;

        return pair01Length >= pair12Length
            ? new RoadMarkingEdgePair(
                [vertices[0], vertices[1]],
                [vertices[3], vertices[2]],
                pair01Length,
                (Distance(positions[0], positions[3]) + Distance(positions[1], positions[2])) * 0.5)
            : new RoadMarkingEdgePair(
                [vertices[1], vertices[2]],
                [vertices[0], vertices[3]],
                pair12Length,
                (Distance(positions[1], positions[0]) + Distance(positions[2], positions[3])) * 0.5);
    }

    private static double Distance(Float3 left, Float3 right)
    {
        double deltaX = left.X - right.X;
        double deltaY = left.Y - right.Y;
        double deltaZ = left.Z - right.Z;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY) + (deltaZ * deltaZ));
    }

    private static Float3 CreateScenePosition(
        GeodeticPoint point,
        GeodeticPoint origin,
        LocalCartesian? cartesian)
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

    private static GeodeticPoint InterpolateAlongEdge(
        GeodeticPoint start,
        GeodeticPoint end,
        double ratio)
    {
        return Lerp(start, end, ratio);
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

    // Adapted from PLATEAU-SDK-for-Unity Runtime/RoadAdjust/RnmModelAdjuster.cs.
    // Each source point moves toward the nearest target point, matching upstream behavior.
    // Upstream MIT license text is stored in THIRD_PARTY_LICENSES/PLATEAU-SDK-for-Unity-LICENSE.txt.
    private static GeodeticPoint[] MoveTowardCrossSection(
        GeodeticPoint[] sourceWay,
        GeodeticPoint[] targetWay,
        Float3[] sourcePositions,
        Float3[] targetPositions,
        double distance)
    {
        if (sourceWay.Length != 2
            || targetWay.Length != 2
            || sourcePositions.Length != 2
            || targetPositions.Length != 2
            || distance <= 0.0)
        {
            return sourceWay.ToArray();
        }

        GeodeticPoint[] moved = new GeodeticPoint[2];
        for (int index = 0; index < 2; index++)
        {
            GeodeticPoint source = sourceWay[index];
            int nearestTargetIndex = 0;
            double nearestDistanceSquared = double.MaxValue;
            for (int targetIndex = 0; targetIndex < targetPositions.Length; targetIndex++)
            {
                double deltaX = sourcePositions[index].X - targetPositions[targetIndex].X;
                double deltaY = sourcePositions[index].Y - targetPositions[targetIndex].Y;
                double deltaZ = sourcePositions[index].Z - targetPositions[targetIndex].Z;
                double distanceSquared = (deltaX * deltaX) + (deltaY * deltaY) + (deltaZ * deltaZ);
                if (distanceSquared < nearestDistanceSquared)
                {
                    nearestDistanceSquared = distanceSquared;
                    nearestTargetIndex = targetIndex;
                }
            }

            GeodeticPoint target = targetWay[nearestTargetIndex];
            double actualDistance = Math.Sqrt(nearestDistanceSquared);
            if (actualDistance <= 1e-8)
            {
                moved[index] = source;
                continue;
            }

            double moveRatio = Math.Min(distance, actualDistance) / actualDistance;
            moved[index] = Lerp(source, target, moveRatio);
        }

        return moved;
    }

    private sealed record RoadMarkingEdgePair(
        GeodeticPoint[] Side0,
        GeodeticPoint[] Side1,
        double Length,
        double Width);
}
