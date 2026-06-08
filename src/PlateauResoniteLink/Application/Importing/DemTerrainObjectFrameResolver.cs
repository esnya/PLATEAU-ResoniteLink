using System.Collections.Generic;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class DemTerrainObjectFrameResolver
{
    public static GeodeticPoint ResolveRequiredThirdMeshOrigin(
        string actualMeshCode,
        IEnumerable<GeodeticPoint> fallbackVertices)
    {
        if (ThirdRegionalMeshCode.TryParse(actualMeshCode, out _)
            && PlateauMeshCode.TryGetGeodeticCenter(actualMeshCode, out GeodeticCoordinate meshCenter))
        {
            return new GeodeticPoint(
                meshCenter.Latitude,
                meshCenter.Longitude,
                meshCenter.Altitude);
        }

        return CityObjectOriginResolver.Resolve(null, fallbackVertices);
    }
}
