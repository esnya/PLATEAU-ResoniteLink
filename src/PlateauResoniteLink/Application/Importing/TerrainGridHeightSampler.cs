using System;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class TerrainGridHeightSampler
{
    private readonly TerrainGridTriangle[] triangles;
    private readonly TerrainGridSpatialIndex spatialIndex;

    private TerrainGridHeightSampler(
        TerrainGridTriangle[] triangles,
        TerrainGridSpatialIndex spatialIndex)
    {
        this.triangles = triangles;
        this.spatialIndex = spatialIndex;
    }

    public static TerrainGridHeightSampler Create(
        TerrainGridTriangle[] triangles,
        double minX,
        double maxX,
        double minZ,
        double maxZ)
    {
        ArgumentNullException.ThrowIfNull(triangles);

        return new TerrainGridHeightSampler(
            triangles,
            TerrainGridSpatialIndex.Create(
                triangles,
                minX,
                maxX,
                minZ,
                maxZ));
    }

    public bool TrySampleHeight(double x, double z, out double height)
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
