using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using GeographicLib;

using LibTessDotNet;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Application.Importing.Contracts;
using PlateauResoniteLink.Application.Importing.Source;

namespace PlateauResoniteLink.Application.Importing.CityGml;

internal static class CityGmlSurfaceMeshTessellator
{
    internal static SurfaceMeshTessellation Tessellate(SurfaceMeshTessellationRequest request)
    {
        List<(MeshVertex First, MeshVertex Second, MeshVertex Third, string SortKey)> triangles = [];
        bool useVertexColors = request.Material.MaterialType == MaterialType.VertexColor;
        ParsedSurface surface = request.Face.Surface;
        DemUvProjection? generatedDemUvProjection = request.Material.TerrainOverlay is not null
            ? request.DemUvProjection
            : null;
        bool useGeneratedDemUv = generatedDemUvProjection is not null;
        SurfaceUvProjection? generatedSurfaceUvProjection = !useGeneratedDemUv
            && surface.TexturePayload is null
            && request.Material.Projection == MaterialProjection.Uv
                ? CreateGeneratedSurfaceUvProjection(
                    surface,
                    request.PackageName,
                    request.CityObjectOrigin,
                    request.CityObjectCartesian,
                    request.FacadeUvProjectionContext)
                : null;
        List<TessellatedRing> tessellatedRings = CreateSurfaceTessellatedRings(
            surface,
            request.CityObjectOrigin,
            request.CityObjectCartesian,
            generatedDemUvProjection,
            generatedSurfaceUvProjection,
            useVertexColors ? surface.BaseColor : null);
        if (tessellatedRings.Count == 0)
        {
            return SurfaceMeshTessellation.Empty;
        }

        Float3? expectedNormal = ComputePolygonNormal(tessellatedRings[0].Vertices.Select(static vertex => vertex.Position));
        if (expectedNormal is null)
        {
            return SurfaceMeshTessellation.Empty;
        }

        (Float3 planeOrigin, Float3 basisX, Float3 basisY) = CreateSurfacePlane(tessellatedRings[0].Vertices);
        Tess tessellator = new();

        foreach (TessellatedRing ring in tessellatedRings)
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

            TessVertexPayload vertex0 = GetTessVertexPayload(tessellator, element0);
            TessVertexPayload vertex1 = GetTessVertexPayload(tessellator, element1);
            TessVertexPayload vertex2 = GetTessVertexPayload(tessellator, element2);

            Float3 position0 = vertex0.Position;
            Float3 position1 = vertex1.Position;
            Float3 position2 = vertex2.Position;
            Float2 uv0 = vertex0.UV;
            Float2 uv1 = vertex1.UV;
            Float2 uv2 = vertex2.UV;
            ColorRgba? color0 = vertex0.Color;
            ColorRgba? color1 = vertex1.Color;
            ColorRgba? color2 = vertex2.Color;

            Float3? triangleNormal = ComputeNormal(position0, position1, position2);
            if (triangleNormal is null)
            {
                continue;
            }

            if (Dot(triangleNormal, expectedNormal) < 0.0)
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

            if (string.Equals(request.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
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

            (MeshVertex first, MeshVertex second, MeshVertex third, string sortKey) =
                CreateCanonicalSurfaceTriangle(
                    CreateMeshVertex(position0, resoniteNormal, uv0, color0),
                    CreateMeshVertex(position1, resoniteNormal, uv1, color1),
                    CreateMeshVertex(position2, resoniteNormal, uv2, color2));
            triangles.Add((first, second, third, sortKey));
        }

        List<MeshVertex> vertices = [];
        List<int> indices = [];
        triangles.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.SortKey, right.SortKey));
        foreach ((MeshVertex first, MeshVertex second, MeshVertex third, _) in triangles)
        {
            int baseIndex = vertices.Count;
            vertices.Add(first);
            vertices.Add(second);
            vertices.Add(third);
            indices.Add(baseIndex);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex + 1);
        }

        return new SurfaceMeshTessellation(vertices.ToArray(), indices.ToArray());
    }

    private static (
        MeshVertex First,
        MeshVertex Second,
        MeshVertex Third,
        string SortKey) CreateCanonicalSurfaceTriangle(
        MeshVertex first,
        MeshVertex second,
        MeshVertex third)
    {
        (MeshVertex First, MeshVertex Second, MeshVertex Third) best = (first, second, third);
        string bestKey = CreateTriangleSortKey(first, second, third);

        string rotatedLeftKey = CreateTriangleSortKey(second, third, first);
        if (StringComparer.Ordinal.Compare(rotatedLeftKey, bestKey) < 0)
        {
            best = (second, third, first);
            bestKey = rotatedLeftKey;
        }

        string rotatedRightKey = CreateTriangleSortKey(third, first, second);
        if (StringComparer.Ordinal.Compare(rotatedRightKey, bestKey) < 0)
        {
            best = (third, first, second);
            bestKey = rotatedRightKey;
        }

        return (best.First, best.Second, best.Third, bestKey);
    }

    private static string CreateTriangleSortKey(
        MeshVertex first,
        MeshVertex second,
        MeshVertex third)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{CreateVertexSortKey(first)}|{CreateVertexSortKey(second)}|{CreateVertexSortKey(third)}");
    }

    private static string CreateVertexSortKey(MeshVertex vertex)
    {
        ColorRgba? color = vertex.Color;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{vertex.Position.X:R},{vertex.Position.Y:R},{vertex.Position.Z:R}|"
            + $"{vertex.Normal.X:R},{vertex.Normal.Y:R},{vertex.Normal.Z:R}|"
            + $"{vertex.UV0.X:R},{vertex.UV0.Y:R}|"
            + $"{color?.R ?? double.NaN:R},{color?.G ?? double.NaN:R},{color?.B ?? double.NaN:R},{color?.A ?? double.NaN:R}");
    }

    private static MeshVertex CreateMeshVertex(
        Float3 position,
        Float3 normal,
        Float2 uv,
        ColorRgba? color)
    {
        return new MeshVertex(
            position,
            normal,
            uv,
            color);
    }

    private static List<TessellatedRing> CreateSurfaceTessellatedRings(
        ParsedSurface surface,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        DemUvProjection? generatedDemUvProjection,
        SurfaceUvProjection? generatedSurfaceUvProjection,
        ColorRgba? vertexColor)
    {
        List<TessellatedRing> rings =
        [
            CreateTessellatedRing(
                surface.ExteriorRing,
                cityObjectOrigin,
                cityObjectCartesian,
                generatedDemUvProjection,
                generatedSurfaceUvProjection,
                vertexColor),
        ];
        rings.AddRange(surface.InteriorRings.Select(ring => CreateTessellatedRing(
            ring,
            cityObjectOrigin,
            cityObjectCartesian,
            generatedDemUvProjection,
            generatedSurfaceUvProjection,
            vertexColor)));
        return rings.Where(static ring => ring.Vertices.Count >= 3).ToList();
    }

    private static TessellatedRing CreateTessellatedRing(
        ParsedRing ring,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        DemUvProjection? generatedDemUvProjection,
        SurfaceUvProjection? generatedSurfaceUvProjection,
        ColorRgba? vertexColor)
    {
        TessellatedVertex[] vertices = ring.Vertices
            .Select((point, index) => new TessellatedVertex(
                CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian),
                generatedDemUvProjection is not null
                    ? CreateGeneratedDemUv(point, generatedDemUvProjection.Value)
                    : ring.UVs is not null && index < ring.UVs.Count
                        ? ring.UVs[index]
                        : generatedSurfaceUvProjection is not null
                        ? CreateGeneratedSurfaceUv(point, cityObjectOrigin, cityObjectCartesian, generatedSurfaceUvProjection)
                        : new Float2(0.0, 0.0),
                vertexColor))
            .ToArray();
        return new TessellatedRing(vertices);
    }

    private static Float2 CreateGeneratedDemUv(
        GeodeticPoint point,
        DemUvProjection demUvProjection)
    {
        double pointX = WebMercatorTileMath.LongitudeToNormalizedX(point.Longitude);
        double pointY = WebMercatorTileMath.LatitudeToNormalizedY(point.Latitude);
        double u = (pointX - demUvProjection.West) / demUvProjection.Width;
        double v = (demUvProjection.South - pointY) / demUvProjection.Height;

        return new Float2(u, v);
    }

    private static SurfaceUvProjection? CreateGeneratedSurfaceUvProjection(
        ParsedSurface surface,
        string packageName,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        FacadeUvProjectionContext? facadeUvProjectionContext)
    {
        Float3[] positions = surface.ExteriorRing.Vertices
            .Select(point => CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian))
            .ToArray();
        if (positions.Length < 3)
        {
            return null;
        }

        Float3? normal = ComputePolygonNormal(positions);
        if (normal is null)
        {
            return null;
        }

        SurfaceUvAxes? surfaceAxes = TryCreatePathAlignedSurfaceUvAxes(packageName, positions, normal)
            ?? TryCreateSurfaceUvAxes(normal);
        if (surfaceAxes is null)
        {
            return null;
        }

        double uvScale = PlateauPackageCatalog.IsBuildingPackage(packageName)
            ? 1.0 / Math.Max(facadeUvProjectionContext?.FloorHeightMeters ?? FacadeFloorMetrics.DefaultFloorUnitMeters, 1e-6)
            : 1.0;
        double vOffset = PlateauPackageCatalog.IsBuildingPackage(packageName)
            ? facadeUvProjectionContext is { } context
                ? -(context.MinimumY * uvScale)
                : -(positions.Min(static position => position.Y) * uvScale)
            : 0.0;
        return new SurfaceUvProjection(
            Scale(surfaceAxes.AxisU, uvScale),
            Scale(surfaceAxes.AxisV, uvScale),
            vOffset);
    }

    private static Float2 CreateGeneratedSurfaceUv(
        GeodeticPoint point,
        GeodeticPoint cityObjectOrigin,
        LocalCartesian? cityObjectCartesian,
        SurfaceUvProjection projection)
    {
        Float3 position = CreateScenePosition(point, cityObjectOrigin, cityObjectCartesian);
        double u = Dot(position, projection.AxisU);
        double v = Dot(position, projection.AxisV) + projection.OffsetV;
        return new Float2(u, v);
    }

    private static Float3 Scale(Float3 value, double scalar)
    {
        return new Float3(
            value.X * scalar,
            value.Y * scalar,
            value.Z * scalar);
    }

    private static SurfaceUvAxes? TryCreateSurfaceUvAxes(Float3 normal)
    {
        Float3 verticalAxis = new(0.0, 1.0, 0.0);
        Float3 facadeAxisU = Cross(verticalAxis, normal);
        if (Magnitude(facadeAxisU) >= 1e-8)
        {
            return new SurfaceUvAxes(Normalize(facadeAxisU), verticalAxis);
        }

        Float3[] referenceAxes =
        [
            new Float3(1.0, 0.0, 0.0),
            new Float3(0.0, 0.0, 1.0),
            verticalAxis,
        ];

        foreach (Float3 referenceAxis in referenceAxes.OrderBy(axis => Math.Abs(Dot(normal, axis))))
        {
            Float3 axisU = Cross(referenceAxis, normal);
            if (Magnitude(axisU) < 1e-8)
            {
                continue;
            }

            axisU = Normalize(axisU);
            Float3 axisV = Cross(normal, axisU);
            if (Magnitude(axisV) < 1e-8)
            {
                continue;
            }

            return new SurfaceUvAxes(axisU, Normalize(axisV));
        }

        return null;
    }

    private static SurfaceUvAxes? TryCreatePathAlignedSurfaceUvAxes(
        string packageName,
        Float3[] positions,
        Float3 normal)
    {
        if (!PlateauPackageCatalog.IsPathLikePackage(packageName)
            || positions.Length < 2
            || Math.Abs(normal.Y) < 0.7)
        {
            return null;
        }

        Float3 axisU = Subtract(positions[1], positions[0]);
        double axisULength = 0.0;
        for (int index = 0; index < positions.Length; index++)
        {
            Float3 start = positions[index];
            Float3 end = positions[(index + 1) % positions.Length];
            Float3 edge = Subtract(end, start);
            Float3 planarEdge = Subtract(edge, Multiply(normal, Dot(edge, normal)));
            double edgeLength = Magnitude(planarEdge);
            if (edgeLength <= axisULength)
            {
                continue;
            }

            axisU = planarEdge;
            axisULength = edgeLength;
        }

        if (axisULength < 1e-8)
        {
            return null;
        }

        axisU = Normalize(axisU);
        Float3 axisV = Cross(normal, axisU);
        if (Magnitude(axisV) < 1e-8)
        {
            return null;
        }

        return new SurfaceUvAxes(axisU, Normalize(axisV));
    }

    private static (Float3 Origin, Float3 BasisX, Float3 BasisY) CreateSurfacePlane(
        IReadOnlyList<TessellatedVertex> vertices)
    {
        Float3 origin = vertices[0].Position;
        Float3? normal = ComputePolygonNormal(vertices.Select(static vertex => vertex.Position))
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
            Data = new TessVertexPayload(vertex.Position, vertex.UV, vertex.Color),
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
            if (data[index] is not TessVertexPayload vertexData)
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

        return new TessVertexPayload(
            new Float3(x, y, z),
            new Float2(u, v),
            hasColor ? new ColorRgba(r, g, b, a) : null);
    }

    private static TessVertexPayload GetTessVertexPayload(Tess tessellator, int elementIndex)
    {
        return tessellator.Vertices[elementIndex].Data as TessVertexPayload
            ?? throw new PlateauImportValidationException(["Polygon tessellation produced a vertex without payload data."]);
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

    private static Float3 Subtract(Float3 left, Float3 right)
    {
        return new Float3(
            left.X - right.X,
            left.Y - right.Y,
            left.Z - right.Z);
    }

    private static Float3 Multiply(Float3 vector, double scalar)
    {
        return new Float3(
            vector.X * scalar,
            vector.Y * scalar,
            vector.Z * scalar);
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

    private sealed record TessellatedVertex(
        Float3 Position,
        Float2 UV,
        ColorRgba? Color);

    private sealed record TessellatedRing(
        IReadOnlyList<TessellatedVertex> Vertices);

    private sealed record TessVertexPayload(
        Float3 Position,
        Float2 UV,
        ColorRgba? Color);

    private sealed record SurfaceUvAxes(
        Float3 AxisU,
        Float3 AxisV);

    private sealed record SurfaceUvProjection(
        Float3 AxisU,
        Float3 AxisV,
        double OffsetV);
}

internal sealed record SurfaceMeshTessellationRequest(
    string PackageName,
    ConstructionFace Face,
    ResolvedMaterial Material,
    GeodeticPoint CityObjectOrigin,
    LocalCartesian? CityObjectCartesian,
    FacadeUvProjectionContext? FacadeUvProjectionContext,
    DemUvProjection? DemUvProjection);

internal sealed record SurfaceMeshTessellation(
    MeshVertex[] Vertices,
    int[] Indices)
{
    public static SurfaceMeshTessellation Empty { get; } = new([], []);
}

internal readonly record struct FacadeUvProjectionContext(
    double MinimumY,
    double MaximumY,
    double FloorHeightMeters,
    int FloorCount);

internal readonly record struct DemUvProjection(
    double West,
    double South,
    double Width,
    double Height);
