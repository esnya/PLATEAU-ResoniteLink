using System;
using System.Collections.Generic;
using System.Linq;

using LibTessDotNet;

namespace PlateauResoniteLink.Application.Importing;

internal static class SurfacePolygonTessellator
{
    public static SurfacePolygonTessellation? Tessellate(IReadOnlyList<TessellatedRing> rings)
    {
        if (rings.Count == 0)
        {
            return null;
        }

        Float3? expectedNormal = SurfaceGeometryMath.ComputeNewellNormal(rings[0].Vertices.Select(static vertex => vertex.Position));
        if (expectedNormal is null)
        {
            return null;
        }

        (Float3 planeOrigin, Float3 basisX, Float3 basisY) = CreateSurfacePlane(rings[0].Vertices);
        Tess tessellator = new();

        foreach (TessellatedRing ring in rings)
        {
            ContourVertex[] contour = ring.Vertices
                .Select(vertex => CreateContourVertex(vertex, planeOrigin, basisX, basisY))
                .ToArray();
            tessellator.AddContour(contour, ContourOrientation.Original);
        }

        tessellator.Tessellate(
            WindingRule.EvenOdd,
            ElementType.Polygons,
            polySize: 3,
            CombineTessVertexData);

        List<SurfaceTessellatedTriangle> triangles = [];
        for (int triangleIndex = 0; triangleIndex < tessellator.ElementCount; triangleIndex++)
        {
            int elementBaseIndex = triangleIndex * 3;
            int element0 = tessellator.Elements[elementBaseIndex];
            int element1 = tessellator.Elements[elementBaseIndex + 1];
            int element2 = tessellator.Elements[elementBaseIndex + 2];
            if (element0 < 0 || element1 < 0 || element2 < 0)
            {
                continue;
            }

            triangles.Add(new SurfaceTessellatedTriangle(
                GetTessellatedVertex(tessellator, element0),
                GetTessellatedVertex(tessellator, element1),
                GetTessellatedVertex(tessellator, element2)));
        }

        return new SurfacePolygonTessellation(expectedNormal, triangles);
    }

    private static (Float3 Origin, Float3 BasisX, Float3 BasisY) CreateSurfacePlane(
        IReadOnlyList<TessellatedVertex> vertices)
    {
        Float3 origin = vertices[0].Position;
        Float3? normal = SurfaceGeometryMath.ComputeNewellNormal(vertices.Select(static vertex => vertex.Position))
            ?? throw new PlateauImportValidationException(["Failed to resolve a polygon plane for tessellation."]);

        Float3? basisX = null;
        foreach (TessellatedVertex vertex in vertices.Skip(1))
        {
            Float3 candidate = Subtract(vertex.Position, origin);
            if (Magnitude(candidate) >= 1e-8)
            {
                basisX = Normalize(candidate);
                break;
            }
        }

        if (basisX is null)
        {
            throw new PlateauImportValidationException(["Failed to resolve a polygon basis for tessellation."]);
        }

        Float3 basisY = Normalize(Cross(normal, basisX));
        return (origin, basisX, basisY);
    }

    private static ContourVertex CreateContourVertex(
        TessellatedVertex vertex,
        Float3 planeOrigin,
        Float3 basisX,
        Float3 basisY)
    {
        Float3 delta = Subtract(vertex.Position, planeOrigin);
        double projectedX = Dot(delta, basisX);
        double projectedY = Dot(delta, basisY);

        return new ContourVertex
        {
            Position = new Vec3((float)projectedX, (float)projectedY, 0.0f),
            Data = vertex,
        };
    }

    private static object CombineTessVertexData(Vec3 position, object[] data, float[] weights)
    {
        double x = 0.0;
        double y = 0.0;
        double z = 0.0;
        double u = 0.0;
        double v = 0.0;
        double r = 0.0;
        double g = 0.0;
        double b = 0.0;
        double a = 0.0;
        bool hasColor = false;

        for (int index = 0; index < data.Length; index++)
        {
            if (data[index] is not TessellatedVertex vertexData)
            {
                continue;
            }

            double weight = weights[index];
            x += vertexData.Position.X * weight;
            y += vertexData.Position.Y * weight;
            z += vertexData.Position.Z * weight;
            u += vertexData.UV.X * weight;
            v += vertexData.UV.Y * weight;
            if (vertexData.Color is not null)
            {
                hasColor = true;
                r += vertexData.Color.R * weight;
                g += vertexData.Color.G * weight;
                b += vertexData.Color.B * weight;
                a += vertexData.Color.A * weight;
            }
        }

        return new TessellatedVertex(
            new Float3(x, y, z),
            new Float2(u, v),
            hasColor ? new ColorRgba(r, g, b, a) : null);
    }

    private static TessellatedVertex GetTessellatedVertex(Tess tessellator, int elementIndex)
    {
        return tessellator.Vertices[elementIndex].Data as TessellatedVertex
            ?? throw new PlateauImportValidationException(["Polygon tessellation produced a vertex without payload data."]);
    }

    private static Float3 Subtract(Float3 left, Float3 right)
    {
        return new Float3(
            left.X - right.X,
            left.Y - right.Y,
            left.Z - right.Z);
    }

    private static Float3 Cross(Float3 left, Float3 right)
    {
        return new Float3(
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));
    }

    private static double Dot(Float3 left, Float3 right)
    {
        return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
    }

    private static double Magnitude(Float3 vector)
    {
        return Math.Sqrt(Dot(vector, vector));
    }

    private static Float3 Normalize(Float3 vector)
    {
        double magnitude = Magnitude(vector);
        if (magnitude < 1e-8)
        {
            throw new PlateauImportValidationException(["Attempted to normalize a zero-length polygon vector."]);
        }

        return new Float3(
            vector.X / magnitude,
            vector.Y / magnitude,
            vector.Z / magnitude);
    }
}

internal sealed record SurfacePolygonTessellation(
    Float3 ExpectedNormal,
    IReadOnlyList<SurfaceTessellatedTriangle> Triangles);

internal sealed record SurfaceTessellatedTriangle(
    TessellatedVertex First,
    TessellatedVertex Second,
    TessellatedVertex Third);
