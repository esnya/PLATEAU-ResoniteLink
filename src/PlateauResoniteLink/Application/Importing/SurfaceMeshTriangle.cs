using System;
using System.Globalization;

namespace PlateauResoniteLink.Application.Importing;

internal static class SurfaceMeshTriangle
{
    public static MeshVertex CreateVertex(
        Float3 position,
        Float3 normal,
        Float2 uv,
        ColorRgba? color)
    {
        return new MeshVertex(position, normal, uv, color);
    }

    public static CanonicalSurfaceTriangle CreateCanonical(
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

        return new CanonicalSurfaceTriangle(best.First, best.Second, best.Third, bestKey);
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
}

internal sealed record CanonicalSurfaceTriangle(
    MeshVertex First,
    MeshVertex Second,
    MeshVertex Third,
    string SortKey);
