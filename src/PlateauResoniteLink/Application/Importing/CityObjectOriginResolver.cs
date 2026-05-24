using System;
using System.Collections.Generic;

namespace PlateauResoniteLink.Application.Importing;

internal static class CityObjectOriginResolver
{
    internal static GeodeticPoint Resolve(GeodeticPoint? originOverride, IEnumerable<GeodeticPoint> vertices)
    {
        return Resolve(
            originOverride,
            vertices,
            static point => point.Latitude,
            static point => point.Longitude,
            static point => point.Altitude,
            static (latitude, longitude, altitude) => new GeodeticPoint(latitude, longitude, altitude));
    }

    internal static TOrigin Resolve<TPoint, TOrigin>(
        TOrigin? originOverride,
        IEnumerable<TPoint> vertices,
        Func<TPoint, double> latitudeSelector,
        Func<TPoint, double> longitudeSelector,
        Func<TPoint, double> altitudeSelector,
        Func<double, double, double, TOrigin> createOrigin)
        where TOrigin : class
    {
        if (originOverride is not null)
        {
            return originOverride;
        }

        double minLatitude = 0.0;
        double maxLatitude = 0.0;
        double minLongitude = 0.0;
        double maxLongitude = 0.0;
        double minAltitude = 0.0;
        bool hasPoint = false;

        foreach (TPoint point in vertices)
        {
            double latitude = latitudeSelector(point);
            double longitude = longitudeSelector(point);
            double altitude = altitudeSelector(point);
            if (!hasPoint)
            {
                minLatitude = latitude;
                maxLatitude = latitude;
                minLongitude = longitude;
                maxLongitude = longitude;
                minAltitude = altitude;
                hasPoint = true;
                continue;
            }

            minLatitude = double.Min(minLatitude, latitude);
            maxLatitude = MaxLikeEnumerable(maxLatitude, latitude);
            minLongitude = double.Min(minLongitude, longitude);
            maxLongitude = MaxLikeEnumerable(maxLongitude, longitude);
            minAltitude = double.Min(minAltitude, altitude);
        }

        if (!hasPoint)
        {
            throw new InvalidOperationException("Sequence contains no elements.");
        }

        return createOrigin(
            (minLatitude + maxLatitude) / 2.0,
            (minLongitude + maxLongitude) / 2.0,
            minAltitude);
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
