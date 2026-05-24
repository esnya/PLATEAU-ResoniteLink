using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class CityObjectGeographicBoundsResolver
{
    internal static GeographicRectangle Resolve(IEnumerable<GeodeticPoint> vertices)
    {
        List<GeodeticPoint> allPoints = vertices.ToList();
        return new GeographicRectangle(
            MinLatitude: allPoints.Min(static point => point.Latitude),
            MaxLatitude: allPoints.Max(static point => point.Latitude),
            MinLongitude: allPoints.Min(static point => point.Longitude),
            MaxLongitude: allPoints.Max(static point => point.Longitude));
    }
}
