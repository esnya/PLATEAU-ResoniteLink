using System;

using ProjectionGeodeticPoint = PlateauResoniteLink.Application.Importing.LocalCityGmlObjectProjection.GeodeticPoint;
using ProjectionParsedRing = PlateauResoniteLink.Application.Importing.LocalCityGmlObjectProjection.ParsedRing;

namespace PlateauResoniteLink.Application.Importing;

internal static class RoadSurfaceEdgePairSelector
{
    internal static EdgePairSelection Select(
        ProjectionGeodeticPoint[] vertices,
        Float3[] positions)
    {
        ValidateQuadInputs(vertices, positions);

        double edge01 = Distance(positions[0], positions[1]);
        double edge12 = Distance(positions[1], positions[2]);
        double edge23 = Distance(positions[2], positions[3]);
        double edge30 = Distance(positions[3], positions[0]);

        double pair01Length = (edge01 + edge23) * 0.5;
        double pair12Length = (edge12 + edge30) * 0.5;

        return pair01Length >= pair12Length
            ? new EdgePairSelection(
                [vertices[0], vertices[1]],
                [vertices[3], vertices[2]],
                [positions[0], positions[1]],
                [positions[3], positions[2]],
                Side0Uvs: null,
                Side1Uvs: null,
                pair01Length,
                (Distance(positions[0], positions[3]) + Distance(positions[1], positions[2])) * 0.5,
                edge01,
                edge23)
            : new EdgePairSelection(
                [vertices[1], vertices[2]],
                [vertices[0], vertices[3]],
                [positions[1], positions[2]],
                [positions[0], positions[3]],
                Side0Uvs: null,
                Side1Uvs: null,
                pair12Length,
                (Distance(positions[1], positions[0]) + Distance(positions[2], positions[3])) * 0.5,
                edge12,
                edge30);
    }

    internal static ParsedEdgePairSelection Select(
        GeodeticPoint[] vertices,
        Float3[] positions)
    {
        ValidateQuadInputs(vertices, positions);

        double edge01 = Distance(positions[0], positions[1]);
        double edge12 = Distance(positions[1], positions[2]);
        double edge23 = Distance(positions[2], positions[3]);
        double edge30 = Distance(positions[3], positions[0]);

        double pair01Length = (edge01 + edge23) * 0.5;
        double pair12Length = (edge12 + edge30) * 0.5;

        return pair01Length >= pair12Length
            ? new ParsedEdgePairSelection(
                [vertices[0], vertices[1]],
                [vertices[3], vertices[2]],
                pair01Length,
                (Distance(positions[0], positions[3]) + Distance(positions[1], positions[2])) * 0.5)
            : new ParsedEdgePairSelection(
                [vertices[1], vertices[2]],
                [vertices[0], vertices[3]],
                pair12Length,
                (Distance(positions[1], positions[0]) + Distance(positions[2], positions[3])) * 0.5);
    }

    internal static EdgePairSelection Select(
        ProjectionParsedRing ring,
        Float3[] positions)
    {
        ArgumentNullException.ThrowIfNull(ring);

        EdgePairSelection pair = Select(ring.Vertices, positions);
        if (ring.UVs is null || ring.UVs.Count != ring.Vertices.Length)
        {
            return pair;
        }

        bool usesFirstEdge = AreSamePoint(pair.Side0[0], ring.Vertices[0])
            && AreSamePoint(pair.Side0[1], ring.Vertices[1]);

        return usesFirstEdge
            ? pair with
            {
                Side0Uvs = [ring.UVs[0], ring.UVs[1]],
                Side1Uvs = [ring.UVs[3], ring.UVs[2]],
            }
            : pair with
            {
                Side0Uvs = [ring.UVs[1], ring.UVs[2]],
                Side1Uvs = [ring.UVs[0], ring.UVs[3]],
            };
    }

    private static double Distance(Float3 left, Float3 right)
    {
        double deltaX = left.X - right.X;
        double deltaY = left.Y - right.Y;
        double deltaZ = left.Z - right.Z;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY) + (deltaZ * deltaZ));
    }

    private static void ValidateQuadInputs<TPoint>(TPoint[] vertices, Float3[] positions)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(positions);
        if (vertices.Length != 4)
        {
            throw new ArgumentException("Road surface edge-pair selection requires exactly four vertices.", nameof(vertices));
        }

        if (positions.Length != 4)
        {
            throw new ArgumentException("Road surface edge-pair selection requires exactly four positions.", nameof(positions));
        }
    }

    private static bool AreSamePoint(ProjectionGeodeticPoint left, ProjectionGeodeticPoint right)
    {
        return Math.Abs(left.Latitude - right.Latitude) < 1e-8
            && Math.Abs(left.Longitude - right.Longitude) < 1e-8
            && Math.Abs(left.Altitude - right.Altitude) < 1e-8;
    }
}

internal sealed record EdgePairSelection(
    ProjectionGeodeticPoint[] Side0,
    ProjectionGeodeticPoint[] Side1,
    Float3[] Side0Positions,
    Float3[] Side1Positions,
    Float2[]? Side0Uvs,
    Float2[]? Side1Uvs,
    double Length,
    double Width,
    double Side0EdgeLength,
    double Side1EdgeLength);

internal sealed record ParsedEdgePairSelection(
    GeodeticPoint[] Side0,
    GeodeticPoint[] Side1,
    double Length,
    double Width);
