using System;
using System.Collections.Generic;
using System.Linq;

using GeographicLib;

using ProjectionGeodeticPoint = PlateauResoniteLink.Application.Importing.LocalCityGmlObjectProjection.GeodeticPoint;
using ProjectionParsedRing = PlateauResoniteLink.Application.Importing.LocalCityGmlObjectProjection.ParsedRing;
using ProjectionParsedSurface = PlateauResoniteLink.Application.Importing.LocalCityGmlObjectProjection.ParsedSurface;

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

        ProjectionGeodeticPoint projectionOrigin = cityObjectOrigin.ToProjectionModel();
        List<ParsedSurface> markingSurfaces = [];
        foreach (ParsedSurface surface in cityObject.Surfaces)
        {
            if (surface.TexturePayload is not null)
            {
                continue;
            }

            List<ProjectionParsedSurface> generatedSurfaces =
                CreateSurfaces(
                    CityGmlProjectionModelAdapter.ToProjectionModel(surface),
                    projectionOrigin,
                    cityObjectCartesian);
            if (generatedSurfaces.Count == 0)
            {
                continue;
            }

            markingSurfaces.AddRange(generatedSurfaces.Select(CityGmlProjectionModelAdapter.FromProjectionModel));
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

    private static List<ProjectionParsedSurface> CreateSurfaces(
        ProjectionParsedSurface surface,
        ProjectionGeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian)
    {
        ProjectionGeodeticPoint[] vertices = surface.ExteriorRing.Vertices;
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

        EdgePairSelection edgePair = RoadSurfaceEdgePairSelector.Select(vertices, positions);
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
        List<ProjectionParsedSurface> segments = new(segmentCount);

        for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            double startT = (double)segmentIndex / segmentCount;
            double endT = (double)(segmentIndex + 1) / segmentCount;
            ProjectionGeodeticPoint side0Start = InterpolateAlongEdge(edgePair.Side0[0], edgePair.Side0[1], startT);
            ProjectionGeodeticPoint side0End = InterpolateAlongEdge(edgePair.Side0[0], edgePair.Side0[1], endT);
            ProjectionGeodeticPoint side1Start = InterpolateAlongEdge(edgePair.Side1[0], edgePair.Side1[1], startT);
            ProjectionGeodeticPoint side1End = InterpolateAlongEdge(edgePair.Side1[0], edgePair.Side1[1], endT);

            ProjectionGeodeticPoint[] side0Source = [side0Start, side0End];
            ProjectionGeodeticPoint[] side1Source = [side1Start, side1End];
            Float3[] side0Positions = side0Source
                .Select(point => CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian))
                .ToArray();
            Float3[] side1Positions = side1Source
                .Select(point => CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian))
                .ToArray();

            ProjectionGeodeticPoint[] side0 = MoveTowardCrossSection(
                side0Source,
                side1Source,
                side0Positions,
                side1Positions,
                insetDistance);
            ProjectionGeodeticPoint[] side1 = MoveTowardCrossSection(
                side1Source,
                side0Source,
                side1Positions,
                side0Positions,
                insetDistance);
            segments.Add(new ProjectionParsedSurface(
                $"{surface.PolygonId}_generated_marking_{segmentIndex:D2}",
                surface.Semantic,
                new ProjectionParsedRing(
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

    private static Float3 CreateScenePosition(
        ProjectionGeodeticPoint point,
        ProjectionGeodeticPoint origin,
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

    private static ProjectionGeodeticPoint InterpolateAlongEdge(
        ProjectionGeodeticPoint start,
        ProjectionGeodeticPoint end,
        double ratio)
    {
        return Lerp(start, end, ratio);
    }

    private static ProjectionGeodeticPoint Lerp(
        ProjectionGeodeticPoint source,
        ProjectionGeodeticPoint target,
        double ratio)
    {
        return new ProjectionGeodeticPoint(
            source.Latitude + ((target.Latitude - source.Latitude) * ratio),
            source.Longitude + ((target.Longitude - source.Longitude) * ratio),
            source.Altitude + ((target.Altitude - source.Altitude) * ratio));
    }

    // Adapted from PLATEAU-SDK-for-Unity Runtime/RoadAdjust/RnmModelAdjuster.cs.
    // Each source point moves toward the nearest target point, matching upstream behavior.
    // Upstream MIT license text is stored in THIRD_PARTY_LICENSES/PLATEAU-SDK-for-Unity-LICENSE.txt.
    private static ProjectionGeodeticPoint[] MoveTowardCrossSection(
        ProjectionGeodeticPoint[] sourceWay,
        ProjectionGeodeticPoint[] targetWay,
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

        ProjectionGeodeticPoint[] moved = new ProjectionGeodeticPoint[2];
        for (int index = 0; index < 2; index++)
        {
            ProjectionGeodeticPoint source = sourceWay[index];
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

            ProjectionGeodeticPoint target = targetWay[nearestTargetIndex];
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
}
