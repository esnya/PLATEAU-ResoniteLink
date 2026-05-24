using System;
using System.Collections.Generic;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class CityObjectGeographicBoundsResolver
{
    internal static GeographicRectangle Resolve(IEnumerable<GeodeticPoint> vertices)
    {
        return Resolve(
            vertices,
            static point => point.Latitude,
            static point => point.Longitude);
    }

    internal static GeographicRectangle Resolve<TPoint>(
        IEnumerable<TPoint> vertices,
        Func<TPoint, double> latitudeSelector,
        Func<TPoint, double> longitudeSelector)
    {
        double minLatitude = 0.0;
        double maxLatitude = 0.0;
        double minLongitude = 0.0;
        double maxLongitude = 0.0;
        bool hasPoint = false;

        foreach (TPoint point in vertices)
        {
            double latitude = latitudeSelector(point);
            double longitude = longitudeSelector(point);
            if (!hasPoint)
            {
                minLatitude = latitude;
                maxLatitude = latitude;
                minLongitude = longitude;
                maxLongitude = longitude;
                hasPoint = true;
                continue;
            }

            minLatitude = double.Min(minLatitude, latitude);
            maxLatitude = MaxLikeEnumerable(maxLatitude, latitude);
            minLongitude = double.Min(minLongitude, longitude);
            maxLongitude = MaxLikeEnumerable(maxLongitude, longitude);
        }

        if (!hasPoint)
        {
            throw new InvalidOperationException("Sequence contains no elements.");
        }

        return new GeographicRectangle(
            MinLatitude: minLatitude,
            MaxLatitude: maxLatitude,
            MinLongitude: minLongitude,
            MaxLongitude: maxLongitude);
    }

    private static double MaxLikeEnumerable(double current, double candidate)
    {
        if (double.IsNaN(candidate))
        {
            return current;
        }

        if (double.IsNaN(current) || candidate > current)
        {
            return candidate;
        }

        return current;
    }
}
