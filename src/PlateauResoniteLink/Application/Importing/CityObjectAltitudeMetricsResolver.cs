using System;
using System.Collections.Generic;

namespace PlateauResoniteLink.Application.Importing;

internal static class CityObjectAltitudeMetricsResolver
{
    internal static double GetMinimumAltitude(IEnumerable<GeodeticPoint> vertices)
    {
        return GetMinimumAltitude(vertices, static vertex => vertex.Altitude);
    }

    internal static double GetMinimumAltitude<TPoint>(
        IEnumerable<TPoint> vertices,
        Func<TPoint, double> altitudeSelector)
    {
        bool hasVertex = false;
        double minAltitude = double.PositiveInfinity;
        foreach (TPoint vertex in vertices)
        {
            hasVertex = true;
            minAltitude = double.Min(minAltitude, altitudeSelector(vertex));
        }

        if (!hasVertex)
        {
            throw new InvalidOperationException("Sequence contains no elements.");
        }

        return minAltitude;
    }

    internal static double? TryGetGeometryHeightMeters(IEnumerable<GeodeticPoint> vertices)
    {
        return TryGetGeometryHeightMeters(vertices, static vertex => vertex.Altitude);
    }

    internal static double? TryGetGeometryHeightMeters<TPoint>(
        IEnumerable<TPoint> vertices,
        Func<TPoint, double> altitudeSelector)
    {
        double minAltitude = double.PositiveInfinity;
        double maxAltitude = double.NegativeInfinity;
        foreach (TPoint vertex in vertices)
        {
            double altitude = altitudeSelector(vertex);
            minAltitude = double.Min(minAltitude, altitude);
            maxAltitude = double.Max(maxAltitude, altitude);
        }

        if (double.IsInfinity(minAltitude) || double.IsInfinity(maxAltitude))
        {
            return null;
        }

        double height = maxAltitude - minAltitude;
        return height > 0.0 ? height : null;
    }
}
