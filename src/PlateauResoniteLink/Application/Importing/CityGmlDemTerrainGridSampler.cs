using System;
using System.Collections.Generic;
using System.Threading;

namespace PlateauResoniteLink.Application.Importing;

internal static class CityGmlDemTerrainGridSampler
{
    internal static DemTerrainGridHeightSamples Sample(
        double minX,
        double maxX,
        double minZ,
        double maxZ,
        double metersPerVertex,
        int maxResolution,
        double fallbackHeight,
        IReadOnlyList<TerrainGridTriangle> triangles,
        CancellationToken cancellationToken = default)
    {
        double extentX = maxX - minX;
        double extentZ = maxZ - minZ;
        int width = Math.Clamp(
            (int)Math.Ceiling(extentX / metersPerVertex) + 1,
            2,
            maxResolution);
        int height = Math.Clamp(
            (int)Math.Ceiling(extentZ / metersPerVertex) + 1,
            2,
            maxResolution);
        double[] localHeights = new double[width * height];
        TerrainGridSampleCoverage[] sampleCoverage = new TerrainGridSampleCoverage[width * height];
        TerrainGridSpatialIndex spatialIndex = TerrainGridSpatialIndex.Create(
            triangles,
            minX,
            maxX,
            minZ,
            maxZ);

        for (int zIndex = 0; zIndex < height; zIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double v = ResolveGridCoordinateRatio(zIndex, height);
            double sampleZ = minZ + (extentZ * v);
            for (int xIndex = 0; xIndex < width; xIndex++)
            {
                double u = ResolveGridCoordinateRatio(xIndex, width);
                double sampleX = minX + (extentX * u);
                int sampleIndex = (zIndex * width) + xIndex;
                if (TrySampleLocalHeight(sampleX, sampleZ, triangles, spatialIndex, out double localHeight))
                {
                    localHeights[sampleIndex] = localHeight;
                    sampleCoverage[sampleIndex] = TerrainGridSampleCoverage.Measured;
                }
                else
                {
                    localHeights[sampleIndex] = fallbackHeight;
                    sampleCoverage[sampleIndex] = TerrainGridSampleCoverage.NoSurface;
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new DemTerrainGridHeightSamples(width, height, localHeights, sampleCoverage);
    }

    internal static bool TrySampleLocalHeight(
        double x,
        double z,
        IReadOnlyList<TerrainGridTriangle> triangles,
        TerrainGridSpatialIndex spatialIndex,
        out double height)
    {
        foreach (int triangleIndex in spatialIndex.GetCandidateTriangleIndices(x, z))
        {
            TerrainGridTriangle triangle = triangles[triangleIndex];
            if (TryInterpolateLocalTriangleHeight(triangle, x, z, out height))
            {
                return true;
            }
        }

        height = 0.0;
        return false;
    }

    private static double ResolveGridCoordinateRatio(int index, int count)
    {
        return count <= 1 ? 0.0 : (double)index / (count - 1);
    }

    private static bool TryInterpolateLocalTriangleHeight(
        TerrainGridTriangle triangle,
        double x,
        double z,
        out double height)
    {
        double denominator = ((triangle.B.Z - triangle.C.Z) * (triangle.A.X - triangle.C.X))
            + ((triangle.C.X - triangle.B.X) * (triangle.A.Z - triangle.C.Z));
        if (Math.Abs(denominator) < 1e-8)
        {
            height = 0.0;
            return false;
        }

        double weight0 = (((triangle.B.Z - triangle.C.Z) * (x - triangle.C.X))
            + ((triangle.C.X - triangle.B.X) * (z - triangle.C.Z))) / denominator;
        double weight1 = (((triangle.C.Z - triangle.A.Z) * (x - triangle.C.X))
            + ((triangle.A.X - triangle.C.X) * (z - triangle.C.Z))) / denominator;
        double weight2 = 1.0 - weight0 - weight1;
        if (weight0 < -1e-5 || weight1 < -1e-5 || weight2 < -1e-5)
        {
            height = 0.0;
            return false;
        }

        height = (triangle.A.Y * weight0) + (triangle.B.Y * weight1) + (triangle.C.Y * weight2);
        return true;
    }

}

internal sealed record DemTerrainGridHeightSamples(
    int Width,
    int Height,
    double[] LocalHeights,
    TerrainGridSampleCoverage[] SampleCoverage);

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
