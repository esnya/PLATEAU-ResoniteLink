using System;

namespace PlateauResoniteLink.Application.Importing;

internal static class RoadSurfaceEdgePairSelector
{
    internal static EdgePairSelection Select(RoadSurfaceQuad quad)
    {
        double edge01 = Distance(quad.Position0, quad.Position1);
        double edge12 = Distance(quad.Position1, quad.Position2);
        double edge23 = Distance(quad.Position2, quad.Position3);
        double edge30 = Distance(quad.Position3, quad.Position0);

        double pair01Length = (edge01 + edge23) * 0.5;
        double pair12Length = (edge12 + edge30) * 0.5;

        return pair01Length >= pair12Length
            ? new EdgePairSelection(
                [quad.Vertex0, quad.Vertex1],
                [quad.Vertex3, quad.Vertex2],
                [quad.Position0, quad.Position1],
                [quad.Position3, quad.Position2],
                CreateUvs(quad.Uv0, quad.Uv1),
                CreateUvs(quad.Uv3, quad.Uv2),
                pair01Length,
                (Distance(quad.Position0, quad.Position3) + Distance(quad.Position1, quad.Position2)) * 0.5,
                edge01,
                edge23)
            : new EdgePairSelection(
                [quad.Vertex1, quad.Vertex2],
                [quad.Vertex0, quad.Vertex3],
                [quad.Position1, quad.Position2],
                [quad.Position0, quad.Position3],
                CreateUvs(quad.Uv1, quad.Uv2),
                CreateUvs(quad.Uv0, quad.Uv3),
                pair12Length,
                (Distance(quad.Position1, quad.Position0) + Distance(quad.Position2, quad.Position3)) * 0.5,
                edge12,
                edge30);
    }

    private static Float2[]? CreateUvs(Float2? first, Float2? second)
    {
        return first is not null && second is not null
            ? [first, second]
            : null;
    }

    private static double Distance(Float3 left, Float3 right)
    {
        double deltaX = left.X - right.X;
        double deltaY = left.Y - right.Y;
        double deltaZ = left.Z - right.Z;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY) + (deltaZ * deltaZ));
    }
}

internal readonly record struct RoadSurfaceQuad(
    GeodeticPoint Vertex0,
    GeodeticPoint Vertex1,
    GeodeticPoint Vertex2,
    GeodeticPoint Vertex3,
    Float3 Position0,
    Float3 Position1,
    Float3 Position2,
    Float3 Position3,
    Float2? Uv0,
    Float2? Uv1,
    Float2? Uv2,
    Float2? Uv3)
{
    internal static bool TryCreate(
        ParsedRing ring,
        Float3[] positions,
        out RoadSurfaceQuad quad)
    {
        ArgumentNullException.ThrowIfNull(ring);
        ArgumentNullException.ThrowIfNull(positions);

        Float2[]? uvs = ring.UVs is { Count: 4 }
            ? [ring.UVs[0], ring.UVs[1], ring.UVs[2], ring.UVs[3]]
            : null;
        return TryCreate(ring.Vertices, positions, uvs, out quad);
    }

    private static bool TryCreate(
        GeodeticPoint[] vertices,
        Float3[] positions,
        Float2[]? uvs,
        out RoadSurfaceQuad quad)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(positions);

        if (vertices.Length != 4 || positions.Length != 4)
        {
            quad = default;
            return false;
        }

        quad = new RoadSurfaceQuad(
            vertices[0],
            vertices[1],
            vertices[2],
            vertices[3],
            positions[0],
            positions[1],
            positions[2],
            positions[3],
            uvs?[0],
            uvs?[1],
            uvs?[2],
            uvs?[3]);
        return true;
    }
}

internal sealed record EdgePairSelection(
    GeodeticPoint[] Side0,
    GeodeticPoint[] Side1,
    Float3[] Side0Positions,
    Float3[] Side1Positions,
    Float2[]? Side0Uvs,
    Float2[]? Side1Uvs,
    double Length,
    double Width,
    double Side0EdgeLength,
    double Side1EdgeLength);
