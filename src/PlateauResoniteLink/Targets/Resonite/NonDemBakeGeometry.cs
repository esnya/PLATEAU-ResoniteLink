using System;
using System.Collections.Generic;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class NonDemBakeGeometry
{
    internal static void AppendAtlasGeometry(
        List<ResoniteMeshVertex> vertices,
        List<int> triangleIndices,
        ResoniteFloat3 bakeOrigin,
        ResoniteConstructionCityObject cityObject,
        ResoniteMeshSubmesh submesh,
        TextureUvRect sourceUvBounds,
        NonDemAtlasRect atlasRect,
        int atlasWidth,
        int atlasHeight)
    {
        IReadOnlyList<ResoniteMeshVertex> sourceVertices = cityObject.Mesh.Vertices;
        ResoniteFloat3 cityObjectOffset = Subtract(cityObject.Transform.Position, bakeOrigin);

        foreach (int sourceIndex in submesh.TriangleVertexIndices)
        {
            ResoniteMeshVertex sourceVertex = sourceVertices[sourceIndex];
            ResoniteFloat2 sourceUv = sourceVertex.UV0;
            ResoniteFloat2 atlasUv = MapUvToAtlas(sourceUv, sourceUvBounds, atlasRect, atlasWidth, atlasHeight);
            vertices.Add(sourceVertex with
            {
                Position = Add(sourceVertex.Position, cityObjectOffset),
                UV0 = atlasUv,
            });
            triangleIndices.Add(vertices.Count - 1);
        }
    }

    internal static void AppendOriginalGeometry(
        List<ResoniteMeshVertex> vertices,
        List<int> triangleIndices,
        ResoniteFloat3 bakeOrigin,
        ResoniteConstructionCityObject cityObject,
        ResoniteMeshSubmesh submesh,
        ResoniteColor? vertexColorOverride)
    {
        IReadOnlyList<ResoniteMeshVertex> sourceVertices = cityObject.Mesh.Vertices;
        ResoniteFloat3 cityObjectOffset = Subtract(cityObject.Transform.Position, bakeOrigin);
        foreach (int sourceIndex in submesh.TriangleVertexIndices)
        {
            ResoniteMeshVertex sourceVertex = sourceVertices[sourceIndex];
            vertices.Add(sourceVertex with
            {
                Position = Add(sourceVertex.Position, cityObjectOffset),
                Color = vertexColorOverride ?? sourceVertex.Color,
            });
            triangleIndices.Add(vertices.Count - 1);
        }
    }

    internal static ResoniteFloat3 ComputeBakeOrigin(IEnumerable<ResoniteConstructionCityObject> cityObjects)
    {
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double minZ = double.PositiveInfinity;

        foreach (ResoniteConstructionCityObject cityObject in cityObjects)
        {
            foreach (ResoniteMeshVertex vertex in cityObject.Mesh.Vertices)
            {
                ResoniteFloat3 worldPosition = Add(vertex.Position, cityObject.Transform.Position);
                minX = Math.Min(minX, worldPosition.X);
                minY = Math.Min(minY, worldPosition.Y);
                minZ = Math.Min(minZ, worldPosition.Z);
            }
        }

        return double.IsPositiveInfinity(minX)
            ? new ResoniteFloat3(0.0, 0.0, 0.0)
            : new ResoniteFloat3(minX, minY, minZ);
    }

    internal static TextureUvRect ComputeUvBounds(
        IReadOnlyList<ResoniteMeshVertex> vertices,
        ResoniteMeshSubmesh submesh)
    {
        double minU = double.PositiveInfinity;
        double minV = double.PositiveInfinity;
        double maxU = double.NegativeInfinity;
        double maxV = double.NegativeInfinity;

        foreach (int sourceIndex in submesh.TriangleVertexIndices)
        {
            ResoniteFloat2 transformedUv = vertices[sourceIndex].UV0;
            minU = Math.Min(minU, transformedUv.X);
            minV = Math.Min(minV, transformedUv.Y);
            maxU = Math.Max(maxU, transformedUv.X);
            maxV = Math.Max(maxV, transformedUv.Y);
        }

        if (double.IsPositiveInfinity(minU) || double.IsPositiveInfinity(minV))
        {
            return TextureUvRect.Identity;
        }

        double width = Math.Max(1.0 / 1024.0, maxU - minU);
        double height = Math.Max(1.0 / 1024.0, maxV - minV);
        return new TextureUvRect(minU, minV, width, height);
    }

    private static ResoniteFloat2 MapUvToAtlas(
        ResoniteFloat2 sourceUv,
        TextureUvRect uvBounds,
        NonDemAtlasRect atlasRect,
        double atlasWidth,
        double atlasHeight)
    {
        TextureUvRect atlasUvRect = TextureUvRect.FromTopLeftPixelRect(
            atlasRect.X,
            atlasRect.Y,
            atlasRect.Width,
            atlasRect.Height,
            (int)Math.Round(atlasWidth),
            (int)Math.Round(atlasHeight));
        ScalarPair remapped = TextureUvRect.RemapValue(
            new ScalarPair(sourceUv.X, sourceUv.Y),
            uvBounds,
            atlasUvRect);
        return new ResoniteFloat2(remapped.X, remapped.Y);
    }

    private static ResoniteFloat3 Add(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return new ResoniteFloat3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    }

    private static ResoniteFloat3 Subtract(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return new ResoniteFloat3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    }
}
