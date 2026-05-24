using System.Collections.Generic;
using System.Linq;

namespace PlateauResoniteLink.Application.Importing;

internal static class CityObjectOriginResolver
{
    internal static GeodeticPoint Resolve(GeodeticPoint? originOverride, IEnumerable<GeodeticPoint> vertices)
    {
        if (originOverride is not null)
        {
            return originOverride;
        }

        List<GeodeticPoint> allPoints = vertices.ToList();
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
}
