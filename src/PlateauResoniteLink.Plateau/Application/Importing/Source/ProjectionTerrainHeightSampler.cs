using System;
using System.Collections.Generic;
using System.Linq;

using GeographicLib;

namespace PlateauResoniteLink.Plateau.Application.Importing.Source;

internal sealed record ProjectionTerrainHeightTriangle(
    GeodeticPoint Vertex0,
    GeodeticPoint Vertex1,
    GeodeticPoint Vertex2);

internal sealed class ProjectionTerrainHeightSampler
{
    private readonly LocalCartesian cartesian;
    private readonly double cellSize;
    private readonly double maxX;
    private readonly double maxZ;
    private readonly double minX;
    private readonly double minZ;
    private readonly int maxCellSearchRadius;
    private readonly TerrainHeightPoint[] points;
    private readonly Dictionary<TerrainGridCell, TerrainHeightPoint[]> pointsByCell;
    private readonly ProjectedTerrainHeightTriangle[] triangles;
    private readonly Dictionary<TerrainGridCell, ProjectedTerrainHeightTriangle[]> trianglesByCell;

    private ProjectionTerrainHeightSampler(
        LocalCartesian cartesian,
        double minX,
        double maxX,
        double minZ,
        double maxZ,
        double cellSize,
        TerrainHeightPoint[] points,
        ProjectedTerrainHeightTriangle[] triangles,
        Dictionary<TerrainGridCell, TerrainHeightPoint[]> pointsByCell,
        Dictionary<TerrainGridCell, ProjectedTerrainHeightTriangle[]> trianglesByCell)
    {
        this.cartesian = cartesian;
        this.cellSize = cellSize;
        this.maxX = maxX;
        this.maxZ = maxZ;
        this.minX = minX;
        this.minZ = minZ;
        maxCellSearchRadius = Math.Max(
            1,
            (int)Math.Ceiling(
                Math.Max(maxX - minX, maxZ - minZ)
                / Math.Max(cellSize, 1e-6)));
        this.points = points;
        this.pointsByCell = pointsByCell;
        this.triangles = triangles;
        this.trianglesByCell = trianglesByCell;
    }

    public static ProjectionTerrainHeightSampler Create(
        IEnumerable<ProjectionTerrainHeightTriangle> sourceTriangles,
        GeodeticPoint origin,
        Geocentric geocentric)
    {
        ArgumentNullException.ThrowIfNull(sourceTriangles);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(geocentric);

        LocalCartesian cartesian = new(
            origin.Latitude,
            origin.Longitude,
            origin.Altitude,
            geocentric);
        List<ProjectedTerrainHeightTriangle> triangles = [];
        List<TerrainHeightPoint> points = [];

        foreach (ProjectionTerrainHeightTriangle triangle in sourceTriangles)
        {
            TerrainHeightPoint point0 = CreatePoint(triangle.Vertex0, cartesian);
            TerrainHeightPoint point1 = CreatePoint(triangle.Vertex1, cartesian);
            TerrainHeightPoint point2 = CreatePoint(triangle.Vertex2, cartesian);
            triangles.Add(new ProjectedTerrainHeightTriangle(
                point0,
                point1,
                point2,
                Math.Min(point0.X, Math.Min(point1.X, point2.X)),
                Math.Max(point0.X, Math.Max(point1.X, point2.X)),
                Math.Min(point0.Z, Math.Min(point1.Z, point2.Z)),
                Math.Max(point0.Z, Math.Max(point1.Z, point2.Z))));
            points.Add(point0);
            points.Add(point1);
            points.Add(point2);
        }

        if (points.Count == 0)
        {
            return new ProjectionTerrainHeightSampler(
                cartesian,
                0.0,
                0.0,
                0.0,
                0.0,
                1.0,
                [],
                [],
                new Dictionary<TerrainGridCell, TerrainHeightPoint[]>(),
                new Dictionary<TerrainGridCell, ProjectedTerrainHeightTriangle[]>());
        }

        double minX = points.Min(static point => point.X);
        double maxX = points.Max(static point => point.X);
        double minZ = points.Min(static point => point.Z);
        double maxZ = points.Max(static point => point.Z);
        double cellSize = ComputeCellSize(minX, maxX, minZ, maxZ, triangles.Count);

        Dictionary<TerrainGridCell, TerrainHeightPoint[]> pointsByCell = CreatePointIndex(points, minX, minZ, cellSize);
        Dictionary<TerrainGridCell, ProjectedTerrainHeightTriangle[]> trianglesByCell =
            CreateTriangleIndex(triangles, minX, minZ, cellSize);

        return new ProjectionTerrainHeightSampler(
            cartesian,
            minX,
            maxX,
            minZ,
            maxZ,
            cellSize,
            points.ToArray(),
            triangles.ToArray(),
            pointsByCell,
            trianglesByCell);
    }

    public bool TrySampleHeight(double latitude, double longitude, out double altitude, bool allowNearestPointFallback = true)
    {
        (double x, double z) = Project(latitude, longitude);
        if (x < minX - 1e-6
            || x > maxX + 1e-6
            || z < minZ - 1e-6
            || z > maxZ + 1e-6)
        {
            altitude = 0.0;
            return false;
        }

        TerrainGridCell cell = GetCell(x, z);
        foreach (ProjectedTerrainHeightTriangle triangle in GetCandidateTriangles(cell))
        {
            if (x < triangle.MinX - 1e-6
                || x > triangle.MaxX + 1e-6
                || z < triangle.MinZ - 1e-6
                || z > triangle.MaxZ + 1e-6)
            {
                continue;
            }

            if (TryInterpolateTriangleHeight(triangle, x, z, out altitude))
            {
                return true;
            }
        }

        foreach (ProjectedTerrainHeightTriangle triangle in GetCandidateTriangles(cell, radius: 1))
        {
            if (x < triangle.MinX - 1e-6
                || x > triangle.MaxX + 1e-6
                || z < triangle.MinZ - 1e-6
                || z > triangle.MaxZ + 1e-6)
            {
                continue;
            }

            if (TryInterpolateTriangleHeight(triangle, x, z, out altitude))
            {
                return true;
            }
        }

        if (allowNearestPointFallback)
        {
            return TrySampleNearestPointHeight(x, z, out altitude);
        }

        altitude = 0.0;
        return false;
    }

    private static TerrainHeightPoint CreatePoint(GeodeticPoint point, LocalCartesian cartesian)
    {
        (double x, _, double z) = cartesian.Forward(point.Latitude, point.Longitude, 0.0);
        return new TerrainHeightPoint(
            point.Latitude,
            point.Longitude,
            point.Altitude,
            x,
            z);
    }

    private (double X, double Z) Project(double latitude, double longitude)
    {
        (double x, _, double z) = cartesian.Forward(latitude, longitude, 0.0);
        return (x, z);
    }

    private static bool TryInterpolateTriangleHeight(
        ProjectedTerrainHeightTriangle triangle,
        double x,
        double z,
        out double altitude)
    {
        double ax = triangle.Vertex0.X;
        double az = triangle.Vertex0.Z;
        double bx = triangle.Vertex1.X;
        double bz = triangle.Vertex1.Z;
        double cx = triangle.Vertex2.X;
        double cz = triangle.Vertex2.Z;

        double denominator = ((bz - cz) * (ax - cx)) + ((cx - bx) * (az - cz));
        if (Math.Abs(denominator) < 1e-8)
        {
            altitude = 0.0;
            return false;
        }

        double weight0 = (((bz - cz) * (x - cx)) + ((cx - bx) * (z - cz))) / denominator;
        double weight1 = (((cz - az) * (x - cx)) + ((ax - cx) * (z - cz))) / denominator;
        double weight2 = 1.0 - weight0 - weight1;
        if (weight0 < -1e-5 || weight1 < -1e-5 || weight2 < -1e-5)
        {
            altitude = 0.0;
            return false;
        }

        altitude = (triangle.Vertex0.Altitude * weight0)
            + (triangle.Vertex1.Altitude * weight1)
            + (triangle.Vertex2.Altitude * weight2);
        return true;
    }

    private bool TrySampleNearestPointHeight(double x, double z, out double altitude)
    {
        if (points.Length == 0)
        {
            altitude = 0.0;
            return false;
        }

        TerrainGridCell cell = GetCell(x, z);
        List<TerrainHeightPoint> candidatePoints = [];
        for (int radius = 0; radius <= maxCellSearchRadius; radius++)
        {
            AppendCandidatePoints(candidatePoints, cell, radius);
            if (candidatePoints.Count >= 4)
            {
                break;
            }
        }

        if (candidatePoints.Count == 0)
        {
            altitude = 0.0;
            return false;
        }

        ReadOnlySpan<TerrainHeightPoint> nearestPoints = SelectNearestPoints(candidatePoints, x, z);
        if (nearestPoints.Length == 0)
        {
            altitude = 0.0;
            return false;
        }

        double weightedAltitude = 0.0;
        double weightSum = 0.0;
        foreach (TerrainHeightPoint point in nearestPoints)
        {
            double distanceSquared = SquaredDistance(point, x, z);
            if (distanceSquared < 1e-8)
            {
                altitude = point.Altitude;
                return true;
            }

            double weight = 1.0 / distanceSquared;
            weightedAltitude += point.Altitude * weight;
            weightSum += weight;
        }

        if (weightSum < 1e-8)
        {
            altitude = 0.0;
            return false;
        }

        altitude = weightedAltitude / weightSum;
        return true;
    }

    private static double ComputeCellSize(
        double minX,
        double maxX,
        double minZ,
        double maxZ,
        int triangleCount)
    {
        if (triangleCount <= 0)
        {
            return 1.0;
        }

        double width = Math.Max(maxX - minX, 1.0);
        double depth = Math.Max(maxZ - minZ, 1.0);
        double area = width * depth;
        double estimatedCellArea = area / triangleCount;
        return Math.Max(1.0, Math.Sqrt(Math.Max(estimatedCellArea, 1e-6)));
    }

    private static Dictionary<TerrainGridCell, TerrainHeightPoint[]> CreatePointIndex(
        IEnumerable<TerrainHeightPoint> points,
        double minX,
        double minZ,
        double cellSize)
    {
        Dictionary<TerrainGridCell, List<TerrainHeightPoint>> buckets = [];

        foreach (TerrainHeightPoint point in points)
        {
            TerrainGridCell cell = GetCell(point.X, point.Z, minX, minZ, cellSize);
            if (!buckets.TryGetValue(cell, out List<TerrainHeightPoint>? bucket))
            {
                bucket = [];
                buckets[cell] = bucket;
            }

            bucket.Add(point);
        }

        return buckets.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ToArray());
    }

    private static Dictionary<TerrainGridCell, ProjectedTerrainHeightTriangle[]> CreateTriangleIndex(
        IEnumerable<ProjectedTerrainHeightTriangle> triangles,
        double minX,
        double minZ,
        double cellSize)
    {
        Dictionary<TerrainGridCell, List<ProjectedTerrainHeightTriangle>> buckets = [];

        foreach (ProjectedTerrainHeightTriangle triangle in triangles)
        {
            TerrainGridCell minCell = GetCell(triangle.MinX, triangle.MinZ, minX, minZ, cellSize);
            TerrainGridCell maxCell = GetCell(triangle.MaxX, triangle.MaxZ, minX, minZ, cellSize);

            for (int x = minCell.X; x <= maxCell.X; x++)
            {
                for (int z = minCell.Z; z <= maxCell.Z; z++)
                {
                    TerrainGridCell cell = new(x, z);
                    if (!buckets.TryGetValue(cell, out List<ProjectedTerrainHeightTriangle>? bucket))
                    {
                        bucket = [];
                        buckets[cell] = bucket;
                    }

                    bucket.Add(triangle);
                }
            }
        }

        return buckets.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ToArray());
    }

    private IEnumerable<ProjectedTerrainHeightTriangle> GetCandidateTriangles(TerrainGridCell centerCell, int radius = 0)
    {
        if (radius == 0)
        {
            if (trianglesByCell.TryGetValue(centerCell, out ProjectedTerrainHeightTriangle[]? localTriangles))
            {
                foreach (ProjectedTerrainHeightTriangle triangle in localTriangles)
                {
                    yield return triangle;
                }
            }

            yield break;
        }

        HashSet<ProjectedTerrainHeightTriangle> seen = new(ProjectedTerrainHeightTriangleReferenceComparer.Instance);
        foreach (TerrainGridCell cell in EnumerateCells(centerCell, radius))
        {
            if (!trianglesByCell.TryGetValue(cell, out ProjectedTerrainHeightTriangle[]? localTriangles))
            {
                continue;
            }

            foreach (ProjectedTerrainHeightTriangle triangle in localTriangles)
            {
                if (seen.Add(triangle))
                {
                    yield return triangle;
                }
            }
        }
    }

    private void AppendCandidatePoints(List<TerrainHeightPoint> destination, TerrainGridCell centerCell, int radius)
    {
        foreach (TerrainGridCell cell in EnumerateCells(centerCell, radius))
        {
            if (!pointsByCell.TryGetValue(cell, out TerrainHeightPoint[]? localPoints))
            {
                continue;
            }

            destination.AddRange(localPoints);
        }
    }

    private static IEnumerable<TerrainGridCell> EnumerateCells(TerrainGridCell centerCell, int radius)
    {
        if (radius == 0)
        {
            yield return centerCell;
            yield break;
        }

        for (int x = centerCell.X - radius; x <= centerCell.X + radius; x++)
        {
            for (int z = centerCell.Z - radius; z <= centerCell.Z + radius; z++)
            {
                if (Math.Abs(x - centerCell.X) != radius
                    && Math.Abs(z - centerCell.Z) != radius)
                {
                    continue;
                }

                yield return new TerrainGridCell(x, z);
            }
        }
    }

    private static TerrainHeightPoint[] SelectNearestPoints(List<TerrainHeightPoint> candidates, double x, double z)
    {
        const int MaxNearestPoints = 4;
        TerrainHeightPoint[] nearestPoints = new TerrainHeightPoint[Math.Min(MaxNearestPoints, candidates.Count)];
        double[] nearestDistances = new double[nearestPoints.Length];
        Array.Fill(nearestDistances, double.PositiveInfinity);
        int count = 0;

        foreach (TerrainHeightPoint candidate in candidates)
        {
            double distanceSquared = SquaredDistance(candidate, x, z);
            int insertIndex = count < nearestPoints.Length
                ? count
                : GetWorstIndex(nearestDistances);

            if (count == nearestPoints.Length && distanceSquared >= nearestDistances[insertIndex])
            {
                continue;
            }

            nearestPoints[insertIndex] = candidate;
            nearestDistances[insertIndex] = distanceSquared;
            if (count < nearestPoints.Length)
            {
                count++;
            }
        }

        if (count == nearestPoints.Length)
        {
            return nearestPoints;
        }

        TerrainHeightPoint[] trimmed = new TerrainHeightPoint[count];
        Array.Copy(nearestPoints, trimmed, count);
        return trimmed;
    }

    private static int GetWorstIndex(double[] distances)
    {
        int worstIndex = 0;
        double worstDistance = distances[0];

        for (int index = 1; index < distances.Length; index++)
        {
            if (distances[index] > worstDistance)
            {
                worstDistance = distances[index];
                worstIndex = index;
            }
        }

        return worstIndex;
    }

    private TerrainGridCell GetCell(double x, double z)
    {
        return GetCell(x, z, minX, minZ, cellSize);
    }

    private static TerrainGridCell GetCell(double x, double z, double minX, double minZ, double cellSize)
    {
        int cellX = (int)Math.Floor((x - minX) / cellSize);
        int cellZ = (int)Math.Floor((z - minZ) / cellSize);
        return new TerrainGridCell(cellX, cellZ);
    }

    private static double SquaredDistance(TerrainHeightPoint point, double x, double z)
    {
        double dx = point.X - x;
        double dz = point.Z - z;
        return (dx * dx) + (dz * dz);
    }

    private sealed record TerrainHeightPoint(
        double Latitude,
        double Longitude,
        double Altitude,
        double X,
        double Z);

    private sealed record ProjectedTerrainHeightTriangle(
        TerrainHeightPoint Vertex0,
        TerrainHeightPoint Vertex1,
        TerrainHeightPoint Vertex2,
        double MinX,
        double MaxX,
        double MinZ,
        double MaxZ);

    private sealed class ProjectedTerrainHeightTriangleReferenceComparer : IEqualityComparer<ProjectedTerrainHeightTriangle>
    {
        internal static readonly ProjectedTerrainHeightTriangleReferenceComparer Instance = new();

        private ProjectedTerrainHeightTriangleReferenceComparer()
        {
        }

        public bool Equals(ProjectedTerrainHeightTriangle? x, ProjectedTerrainHeightTriangle? y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(ProjectedTerrainHeightTriangle obj)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }

    private readonly record struct TerrainGridCell(int X, int Z);
}
