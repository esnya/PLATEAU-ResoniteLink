using System;
using System.Collections.Generic;

namespace PlateauResoniteLink.Application.Importing;

internal sealed record TerrainGridTriangle(
    Float3 A,
    Float3 B,
    Float3 C);

internal sealed class TerrainGridSpatialIndex
{
    private static readonly IReadOnlyList<int> EmptyTriangleIndices = Array.Empty<int>();

    private readonly IReadOnlyList<int>[] triangleBuckets;
    private readonly double minX;
    private readonly double minZ;
    private readonly double inverseCellSizeX;
    private readonly double inverseCellSizeZ;
    private readonly int cellsX;
    private readonly int cellsZ;

    private TerrainGridSpatialIndex(
        IReadOnlyList<int>[] triangleBuckets,
        double minX,
        double minZ,
        double inverseCellSizeX,
        double inverseCellSizeZ,
        int cellsX,
        int cellsZ)
    {
        this.triangleBuckets = triangleBuckets;
        this.minX = minX;
        this.minZ = minZ;
        this.inverseCellSizeX = inverseCellSizeX;
        this.inverseCellSizeZ = inverseCellSizeZ;
        this.cellsX = cellsX;
        this.cellsZ = cellsZ;
    }

    public static TerrainGridSpatialIndex Create(
        IReadOnlyList<TerrainGridTriangle> triangles,
        double minX,
        double maxX,
        double minZ,
        double maxZ)
    {
        if (triangles.Count == 0)
        {
            return new TerrainGridSpatialIndex([EmptyTriangleIndices], minX, minZ, 1.0, 1.0, 1, 1);
        }

        double extentX = Math.Max(maxX - minX, 1e-6);
        double extentZ = Math.Max(maxZ - minZ, 1e-6);
        double aspectRatio = extentX / extentZ;
        double baseCellCount = Math.Ceiling(Math.Sqrt(triangles.Count));
        int cellsX = Math.Clamp((int)Math.Ceiling(baseCellCount * Math.Sqrt(aspectRatio)), 1, 256);
        int cellsZ = Math.Clamp((int)Math.Ceiling(baseCellCount / Math.Sqrt(aspectRatio)), 1, 256);
        double cellSizeX = extentX / cellsX;
        double cellSizeZ = extentZ / cellsZ;
        List<int>[] mutableTriangleBuckets = new List<int>[cellsX * cellsZ];

        for (int triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
        {
            TerrainGridTriangle triangle = triangles[triangleIndex];
            double triangleMinX = Math.Min(triangle.A.X, Math.Min(triangle.B.X, triangle.C.X));
            double triangleMaxX = Math.Max(triangle.A.X, Math.Max(triangle.B.X, triangle.C.X));
            double triangleMinZ = Math.Min(triangle.A.Z, Math.Min(triangle.B.Z, triangle.C.Z));
            double triangleMaxZ = Math.Max(triangle.A.Z, Math.Max(triangle.B.Z, triangle.C.Z));
            int startX = GetCellIndex(triangleMinX, minX, cellSizeX, cellsX);
            int endX = GetCellIndex(triangleMaxX, minX, cellSizeX, cellsX);
            int startZ = GetCellIndex(triangleMinZ, minZ, cellSizeZ, cellsZ);
            int endZ = GetCellIndex(triangleMaxZ, minZ, cellSizeZ, cellsZ);

            for (int cellZ = startZ; cellZ <= endZ; cellZ++)
            {
                for (int cellX = startX; cellX <= endX; cellX++)
                {
                    int bucketIndex = (cellZ * cellsX) + cellX;
                    (mutableTriangleBuckets[bucketIndex] ??= []).Add(triangleIndex);
                }
            }
        }

        IReadOnlyList<int>[] triangleBuckets = new IReadOnlyList<int>[mutableTriangleBuckets.Length];
        for (int bucketIndex = 0; bucketIndex < triangleBuckets.Length; bucketIndex++)
        {
            triangleBuckets[bucketIndex] = mutableTriangleBuckets[bucketIndex]?.ToArray()
                ?? EmptyTriangleIndices;
        }

        return new TerrainGridSpatialIndex(
            triangleBuckets,
            minX,
            minZ,
            1.0 / cellSizeX,
            1.0 / cellSizeZ,
            cellsX,
            cellsZ);
    }

    public IReadOnlyList<int> GetCandidateTriangleIndices(double x, double z)
    {
        int cellX = Math.Clamp((int)((x - minX) * inverseCellSizeX), 0, cellsX - 1);
        int cellZ = Math.Clamp((int)((z - minZ) * inverseCellSizeZ), 0, cellsZ - 1);
        IReadOnlyList<int> bucket = triangleBuckets[(cellZ * cellsX) + cellX];
        return bucket is { Count: > 0 } ? bucket : EmptyTriangleIndices;
    }

    private static int GetCellIndex(double coordinate, double minimum, double cellSize, int cellCount)
    {
        return Math.Clamp((int)((coordinate - minimum) / cellSize), 0, cellCount - 1);
    }
}
