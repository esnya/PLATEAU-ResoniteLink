using System;
using System.Collections.Generic;
using System.Linq;

namespace PlateauResoniteLink.Application.Importing;

internal static class CityObjectGeometryMetrics
{
    public static GeodeticPoint GetCenterOrigin(ParsedCityObject cityObject)
    {
        ArgumentNullException.ThrowIfNull(cityObject);
        if (cityObject.GeodeticOriginOverride is not null)
        {
            return cityObject.GeodeticOriginOverride;
        }

        return CreateCenterOrigin(cityObject.Surfaces.SelectMany(static surface => surface.Vertices));
    }

    public static double? TryGetGeometryHeightMeters(IEnumerable<ParsedSurface> surfaces)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        return TryGetGeometryHeightMeters(surfaces.SelectMany(static surface => surface.Vertices));
    }

    private static GeodeticPoint CreateCenterOrigin(IEnumerable<GeodeticPoint> points)
    {
        List<GeodeticPoint> allPoints = points.ToList();
        double minLatitude = allPoints.Min(static point => point.Latitude);
        double maxLatitude = allPoints.Max(static point => point.Latitude);
        double minLongitude = allPoints.Min(static point => point.Longitude);
        double maxLongitude = allPoints.Max(static point => point.Longitude);
        double minAltitude = allPoints.Min(static point => point.Altitude);

        return new GeodeticPoint(
            Latitude: (minLatitude + maxLatitude) / 2.0,
            Longitude: (minLongitude + maxLongitude) / 2.0,
            Altitude: minAltitude);
    }

    private static double? TryGetGeometryHeightMeters(IEnumerable<GeodeticPoint> points)
    {
        double minAltitude = double.PositiveInfinity;
        double maxAltitude = double.NegativeInfinity;
        foreach (GeodeticPoint vertex in points)
        {
            minAltitude = Math.Min(minAltitude, vertex.Altitude);
            maxAltitude = Math.Max(maxAltitude, vertex.Altitude);
        }

        if (double.IsInfinity(minAltitude) || double.IsInfinity(maxAltitude))
        {
            return null;
        }

        double height = maxAltitude - minAltitude;
        return height > 0.0 ? height : null;
    }
}
