using System;
using System.Collections.Generic;
using System.Linq;

namespace PlateauResoniteLink.Application.Importing;

internal static class MeshCodeBoundsFilter
{
    private const double OverlapTolerance = 1e-10;

    internal static bool IntersectsRequestedAreas(
        IEnumerable<(double Latitude, double Longitude)> points,
        IReadOnlyList<MeshCodeBounds> requestedMeshAreas)
    {
        ArgumentNullException.ThrowIfNull(points);

        double minLatitude = double.PositiveInfinity;
        double maxLatitude = double.NegativeInfinity;
        double minLongitude = double.PositiveInfinity;
        double maxLongitude = double.NegativeInfinity;
        bool hasPoint = false;
        foreach ((double latitude, double longitude) in points)
        {
            hasPoint = true;
            minLatitude = Math.Min(minLatitude, latitude);
            maxLatitude = Math.Max(maxLatitude, latitude);
            minLongitude = Math.Min(minLongitude, longitude);
            maxLongitude = Math.Max(maxLongitude, longitude);
        }

        if (!hasPoint)
        {
            return false;
        }

        return IntersectsRequestedAreas(minLatitude, maxLatitude, minLongitude, maxLongitude, requestedMeshAreas);
    }

    internal static bool IntersectsRequestedAreas(
        MeshCodeBounds meshCodeArea,
        IReadOnlyList<MeshCodeBounds> requestedMeshAreas)
    {
        return IntersectsRequestedAreas(
            meshCodeArea.SouthLatitude,
            meshCodeArea.NorthLatitude,
            meshCodeArea.WestLongitude,
            meshCodeArea.EastLongitude,
            requestedMeshAreas);
    }

    private static bool IntersectsRequestedAreas(
        double minLatitude,
        double maxLatitude,
        double minLongitude,
        double maxLongitude,
        IReadOnlyList<MeshCodeBounds> requestedMeshAreas)
    {
        return requestedMeshAreas.Any(requestedMeshArea =>
        {
            double latitudeOverlap = Math.Min(maxLatitude, requestedMeshArea.NorthLatitude)
                - Math.Max(minLatitude, requestedMeshArea.SouthLatitude);
            if (latitudeOverlap <= OverlapTolerance)
            {
                return false;
            }

            double longitudeOverlap = Math.Min(maxLongitude, requestedMeshArea.EastLongitude)
                - Math.Max(minLongitude, requestedMeshArea.WestLongitude);
            return longitudeOverlap > OverlapTolerance;
        });
    }
}
