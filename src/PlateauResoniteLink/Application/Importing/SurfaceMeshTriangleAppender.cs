using System;
using System.Collections.Generic;

namespace PlateauResoniteLink.Application.Importing;

internal static class SurfaceMeshTriangleAppender
{
    public static void Append(
        string packageName,
        SurfacePolygonTessellation tessellation,
        List<MeshVertex> vertices,
        List<int> indices)
    {
        List<CanonicalSurfaceTriangle> triangles = [];
        foreach (SurfaceTessellatedTriangle tessellatedTriangle in tessellation.Triangles)
        {
            Float3 position0 = tessellatedTriangle.First.Position;
            Float3 position1 = tessellatedTriangle.Second.Position;
            Float3 position2 = tessellatedTriangle.Third.Position;
            Float2 uv0 = tessellatedTriangle.First.UV;
            Float2 uv1 = tessellatedTriangle.Second.UV;
            Float2 uv2 = tessellatedTriangle.Third.UV;
            ColorRgba? color0 = tessellatedTriangle.First.Color;
            ColorRgba? color1 = tessellatedTriangle.Second.Color;
            ColorRgba? color2 = tessellatedTriangle.Third.Color;

            Float3? triangleNormal = ComputeNormal(position0, position1, position2);
            if (triangleNormal is null)
            {
                continue;
            }

            if (Dot(triangleNormal, tessellation.ExpectedNormal) < 0.0)
            {
                (position1, position2) = (position2, position1);
                (uv1, uv2) = (uv2, uv1);
                (color1, color2) = (color2, color1);
                triangleNormal = ComputeNormal(position0, position1, position2);
                if (triangleNormal is null)
                {
                    continue;
                }
            }

            Float3? resoniteNormal = ComputeNormal(position0, position2, position1);
            if (resoniteNormal is null)
            {
                continue;
            }

            if (string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase)
                && resoniteNormal.Y < 0.0)
            {
                (position1, position2) = (position2, position1);
                (uv1, uv2) = (uv2, uv1);
                (color1, color2) = (color2, color1);
                resoniteNormal = ComputeNormal(position0, position2, position1);
                if (resoniteNormal is null)
                {
                    continue;
                }
            }

            triangles.Add(SurfaceMeshTriangle.CreateCanonical(
                SurfaceMeshTriangle.CreateVertex(position0, resoniteNormal, uv0, color0),
                SurfaceMeshTriangle.CreateVertex(position1, resoniteNormal, uv1, color1),
                SurfaceMeshTriangle.CreateVertex(position2, resoniteNormal, uv2, color2)));
        }

        triangles.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.SortKey, right.SortKey));
        foreach (CanonicalSurfaceTriangle triangle in triangles)
        {
            int baseIndex = vertices.Count;
            vertices.Add(triangle.First);
            vertices.Add(triangle.Second);
            vertices.Add(triangle.Third);
            indices.Add(baseIndex);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex + 1);
        }
    }

    private static Float3? ComputeNormal(
        Float3 position0,
        Float3 position1,
        Float3 position2)
    {
        double ax = position1.X - position0.X;
        double ay = position1.Y - position0.Y;
        double az = position1.Z - position0.Z;
        double bx = position2.X - position0.X;
        double by = position2.Y - position0.Y;
        double bz = position2.Z - position0.Z;

        double crossX = ay * bz - az * by;
        double crossY = az * bx - ax * bz;
        double crossZ = ax * by - ay * bx;
        double magnitude = Math.Sqrt((crossX * crossX) + (crossY * crossY) + (crossZ * crossZ));

        if (magnitude < 1e-8)
        {
            return null;
        }

        return new Float3(crossX / magnitude, crossY / magnitude, crossZ / magnitude);
    }

    private static double Dot(Float3 left, Float3 right)
    {
        return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
    }
}
