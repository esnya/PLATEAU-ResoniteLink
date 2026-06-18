using System;
using System.Collections.Generic;
using System.Linq;

namespace PlateauResoniteLink.Plateau.Application.Importing;

internal static class DatasetGeometryVramEstimator
{
    private const int GeometryVertexBytesMin = 32;
    private const int GeometryVertexBytesMax = 64;
    private const int GeometryIndexBytes = 4;
    internal static DatasetGeometryVramEstimate CreateEstimate(
        IReadOnlyDictionary<string, GeometryVramAccumulator> geometryByPackage)
    {
        Dictionary<string, DatasetPackageGeometryVramEstimate> packageEstimates = geometryByPackage
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                static pair => pair.Key,
                static pair => CreatePackageEstimate(pair.Key, pair.Value),
                StringComparer.Ordinal);

        long positionCount = packageEstimates.Values.Sum(static estimate => estimate.PositionCount);
        long triangleCount = packageEstimates.Values.Sum(static estimate => estimate.TriangleCount);
        long vertexBufferBytesMin = positionCount * GeometryVertexBytesMin;
        long vertexBufferBytesMax = positionCount * GeometryVertexBytesMax;
        long indexBufferBytes = triangleCount * 3 * GeometryIndexBytes;

        return new DatasetGeometryVramEstimate(
            positionCount,
            triangleCount,
            vertexBufferBytesMin,
            vertexBufferBytesMax,
            indexBufferBytes,
            vertexBufferBytesMin + indexBufferBytes,
            vertexBufferBytesMax + indexBufferBytes,
            packageEstimates);
    }

    internal static DatasetPackageGeometryVramEstimate CreatePackageEstimate(
        string packageName,
        GeometryVramAccumulator accumulator)
    {
        long vertexBufferBytesMin = accumulator.PositionCount * GeometryVertexBytesMin;
        long vertexBufferBytesMax = accumulator.PositionCount * GeometryVertexBytesMax;
        long indexBufferBytes = accumulator.TriangleCount * 3 * GeometryIndexBytes;

        return new DatasetPackageGeometryVramEstimate(
            packageName,
            accumulator.PositionCount,
            accumulator.TriangleCount,
            vertexBufferBytesMin,
            vertexBufferBytesMax,
            indexBufferBytes,
            vertexBufferBytesMin + indexBufferBytes,
            vertexBufferBytesMax + indexBufferBytes);
    }

    internal static GeometryVramAccumulator GetOrCreateAccumulator(
        Dictionary<string, GeometryVramAccumulator> geometryByPackage,
        string packageName)
    {
        if (!geometryByPackage.TryGetValue(packageName, out GeometryVramAccumulator? accumulator))
        {
            accumulator = new GeometryVramAccumulator();
            geometryByPackage[packageName] = accumulator;
        }

        return accumulator;
    }
}

internal sealed class GeometryVramAccumulator
{
    public long PositionCount { get; set; }

    public long TriangleCount { get; set; }
}
