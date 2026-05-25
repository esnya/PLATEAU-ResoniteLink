using System;
using System.Collections.Generic;
using System.Linq;

using ProjectionGeodeticPoint = PlateauResoniteLink.Application.Importing.LocalCityGmlObjectProjection.GeodeticPoint;
using ProjectionParsedRing = PlateauResoniteLink.Application.Importing.LocalCityGmlObjectProjection.ParsedRing;
using ProjectionParsedSurface = PlateauResoniteLink.Application.Importing.LocalCityGmlObjectProjection.ParsedSurface;

namespace PlateauResoniteLink.Application.Importing;

internal static class TerrainAlignedTransportationSurfaceSplitter
{
    internal const double DefaultSegmentLengthMeters = 5.0;
    internal const double MinSegmentLengthMeters = 2.0;
    internal const double SegmentLengthByWidthRatio = 0.8;

    internal static List<ProjectionParsedSurface> Split(
        ProjectionParsedSurface surface,
        Float3[] positions,
        EdgePairSelection edgePair)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(edgePair);

        double segmentLength = ComputeSegmentLength(edgePair.Width);
        if (edgePair.Length <= segmentLength + 1e-6)
        {
            return [surface];
        }

        List<ProjectionParsedSurface> strips = CreateStrips(surface, positions, edgePair, segmentLength);
        return strips.Count > 0 ? strips : [surface];
    }

    private static double ComputeSegmentLength(double roadWidth)
    {
        double preferredLength = roadWidth * SegmentLengthByWidthRatio;
        return Math.Clamp(
            preferredLength,
            MinSegmentLengthMeters,
            DefaultSegmentLengthMeters);
    }

    private static List<ProjectionParsedSurface> CreateStrips(
        ProjectionParsedSurface surface,
        Float3[] positions,
        EdgePairSelection edgePair,
        double segmentLength)
    {
        Float3 axis = CreateTransportationSurfaceAxis(edgePair);
        if (LengthSquared(axis) < 1e-8)
        {
            return [];
        }

        double minStation = positions.Min(position => DotHorizontal(position, axis));
        double maxStation = positions.Max(position => DotHorizontal(position, axis));
        if (maxStation - minStation <= 1e-6)
        {
            return [];
        }

        SortedSet<double> stations = [minStation, maxStation];
        foreach (Float3 position in positions)
        {
            stations.Add(DotHorizontal(position, axis));
        }

        for (double station = minStation + segmentLength; station < maxStation - 1e-6; station += segmentLength)
        {
            stations.Add(station);
        }

        List<(double Station, SurfaceSliceSample[] Samples)> slices = new(stations.Count);
        foreach (double station in stations)
        {
            SurfaceSliceSample[] samples = IntersectAtStation(surface.ExteriorRing, positions, axis, station);
            if (samples.Length > 0)
            {
                slices.Add((station, samples));
            }
        }

        List<ProjectionParsedSurface> strips = [];
        for (int index = 1; index < slices.Count; index++)
        {
            SurfaceSliceSample[] previousSamples = slices[index - 1].Samples;
            SurfaceSliceSample[] currentSamples = slices[index].Samples;
            if (previousSamples.Length == 2 && currentSamples.Length == 2)
            {
                strips.Add(CreateStripSurface(
                    surface,
                    $"terrain_strip_{index - 1:D2}",
                    previousSamples[0],
                    previousSamples[1],
                    currentSamples[1],
                    currentSamples[0]));
            }
            else if (previousSamples.Length == 1 && currentSamples.Length == 2)
            {
                strips.Add(CreateStripSurface(
                    surface,
                    $"terrain_fan_start_{index - 1:D2}",
                    previousSamples[0],
                    currentSamples[1],
                    currentSamples[0]));
            }
            else if (previousSamples.Length == 2 && currentSamples.Length == 1)
            {
                strips.Add(CreateStripSurface(
                    surface,
                    $"terrain_fan_end_{index - 1:D2}",
                    previousSamples[0],
                    previousSamples[1],
                    currentSamples[0]));
            }
        }

        return strips;
    }

    private static ProjectionParsedSurface CreateStripSurface(
        ProjectionParsedSurface sourceSurface,
        string suffix,
        params SurfaceSliceSample[] samples)
    {
        Float2[]? uvs = null;
        if (samples.All(static sample => sample.UV is not null))
        {
            List<Float2> uvList = new(samples.Length);
            for (int index = 0; index < samples.Length; index++)
            {
                if (samples[index].UV is Float2 uv)
                {
                    uvList.Add(uv);
                }
            }

            uvs = [.. uvList];
        }

        return sourceSurface with
        {
            PolygonId = $"{sourceSurface.PolygonId}_{suffix}",
            ExteriorRing = new ProjectionParsedRing(
                $"{sourceSurface.ExteriorRing.RingId}_{suffix}",
                [.. samples.Select(static sample => sample.Point)],
                uvs),
        };
    }

    private static SurfaceSliceSample[] IntersectAtStation(
        ProjectionParsedRing ring,
        Float3[] positions,
        Float3 axis,
        double station)
    {
        Float3 lateralAxis = new(-axis.Z, 0.0, axis.X);
        List<SurfaceSliceSample> intersections = [];
        for (int index = 0; index < ring.Vertices.Length; index++)
        {
            int nextIndex = (index + 1) % ring.Vertices.Length;
            double startStation = DotHorizontal(positions[index], axis);
            double endStation = DotHorizontal(positions[nextIndex], axis);
            double deltaStation = endStation - startStation;
            if (Math.Abs(deltaStation) < 1e-8)
            {
                if (Math.Abs(station - startStation) > 1e-8)
                {
                    continue;
                }

                TryAddSliceSample(intersections, ring, positions, lateralAxis, index, ring.Vertices[index], 0.0);
                TryAddSliceSample(intersections, ring, positions, lateralAxis, nextIndex, ring.Vertices[nextIndex], 0.0);
                continue;
            }

            double ratio = (station - startStation) / deltaStation;
            if (ratio < -1e-8 || ratio > 1.0 + 1e-8)
            {
                continue;
            }

            ratio = Math.Clamp(ratio, 0.0, 1.0);
            ProjectionGeodeticPoint point = InterpolateAlongEdge(ring.Vertices[index], ring.Vertices[nextIndex], ratio);
            TryAddSliceSample(intersections, ring, positions, lateralAxis, index, point, ratio);
        }

        intersections.Sort(static (left, right) => left.LateralPosition.CompareTo(right.LateralPosition));
        return [.. intersections];
    }

    private static void TryAddSliceSample(
        List<SurfaceSliceSample> intersections,
        ProjectionParsedRing ring,
        Float3[] positions,
        Float3 lateralAxis,
        int edgeStartIndex,
        ProjectionGeodeticPoint point,
        double ratio)
    {
        if (intersections.Any(existing => AreSamePoint(existing.Point, point)))
        {
            return;
        }

        int edgeEndIndex = (edgeStartIndex + 1) % ring.Vertices.Length;
        Float3 position = Lerp(positions[edgeStartIndex], positions[edgeEndIndex], ratio);
        Float2? uv = ring.UVs is not null && ring.UVs.Count == ring.Vertices.Length
            ? Lerp(ring.UVs[edgeStartIndex], ring.UVs[edgeEndIndex], ratio)
            : null;
        intersections.Add(new SurfaceSliceSample(point, uv, DotHorizontal(position, lateralAxis)));
    }

    private static Float3 CreateTransportationSurfaceAxis(EdgePairSelection edgePair)
    {
        Float3 side0Vector = NormalizeHorizontal(Subtract(edgePair.Side0Positions[1], edgePair.Side0Positions[0]));
        Float3 side1Vector = NormalizeHorizontal(Subtract(edgePair.Side1Positions[1], edgePair.Side1Positions[0]));
        if (LengthSquared(side0Vector) < 1e-8)
        {
            return side1Vector;
        }

        if (LengthSquared(side1Vector) < 1e-8)
        {
            return side0Vector;
        }

        if (DotHorizontal(side0Vector, side1Vector) < 0.0)
        {
            side1Vector = new Float3(-side1Vector.X, 0.0, -side1Vector.Z);
        }

        return NormalizeHorizontal(Add(side0Vector, side1Vector));
    }

    private static ProjectionGeodeticPoint InterpolateAlongEdge(
        ProjectionGeodeticPoint start,
        ProjectionGeodeticPoint end,
        double ratio)
    {
        return new ProjectionGeodeticPoint(
            start.Latitude + ((end.Latitude - start.Latitude) * ratio),
            start.Longitude + ((end.Longitude - start.Longitude) * ratio),
            start.Altitude + ((end.Altitude - start.Altitude) * ratio));
    }

    private static Float3 Add(Float3 left, Float3 right)
    {
        return new Float3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    }

    private static Float3 Subtract(Float3 left, Float3 right)
    {
        return new Float3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    }

    private static Float3 NormalizeHorizontal(Float3 value)
    {
        double length = Math.Sqrt((value.X * value.X) + (value.Z * value.Z));
        if (length <= 1e-8)
        {
            return new Float3(0.0, 0.0, 0.0);
        }

        return new Float3(value.X / length, 0.0, value.Z / length);
    }

    private static double DotHorizontal(Float3 left, Float3 right)
    {
        return (left.X * right.X) + (left.Z * right.Z);
    }

    private static double LengthSquared(Float3 value)
    {
        return (value.X * value.X) + (value.Y * value.Y) + (value.Z * value.Z);
    }

    private static Float3 Lerp(Float3 source, Float3 target, double ratio)
    {
        return new Float3(
            source.X + ((target.X - source.X) * ratio),
            source.Y + ((target.Y - source.Y) * ratio),
            source.Z + ((target.Z - source.Z) * ratio));
    }

    private static Float2 Lerp(Float2 source, Float2 target, double ratio)
    {
        return new Float2(
            source.X + ((target.X - source.X) * ratio),
            source.Y + ((target.Y - source.Y) * ratio));
    }

    private static bool AreSamePoint(ProjectionGeodeticPoint left, ProjectionGeodeticPoint right)
    {
        return Math.Abs(left.Latitude - right.Latitude) < 1e-8
            && Math.Abs(left.Longitude - right.Longitude) < 1e-8
            && Math.Abs(left.Altitude - right.Altitude) < 1e-8;
    }

    private readonly record struct SurfaceSliceSample(
        ProjectionGeodeticPoint Point,
        Float2? UV,
        double LateralPosition);
}
