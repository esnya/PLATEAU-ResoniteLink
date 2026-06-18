using System.Collections.Generic;

using PlateauResoniteLink.Domain.Importing;

using LocalCartesian = GeographicLib.LocalCartesian;
using PlateauResoniteLink.Application.Importing.Contracts;
using PlateauResoniteLink.Application.Importing.Source;

namespace PlateauResoniteLink.Application.Importing.Plateau;

internal static class DemTerrainObjectFrameResolver
{
    public static bool TryResolveThirdMeshFrame(
        string actualMeshCode,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        out DemTerrainObjectFrame frame)
    {
        if (!ThirdRegionalMeshCode.TryParse(actualMeshCode, out _)
            || !PlateauMeshCode.TryGetGeodeticCenter(actualMeshCode, out GeodeticCoordinate meshCenter))
        {
            frame = default;
            return false;
        }

        GeodeticPoint origin = new(
            meshCenter.Latitude,
            meshCenter.Longitude,
            meshCenter.Altitude);
        frame = new DemTerrainObjectFrame(
            origin,
            CreateScenePosition(origin, globalOriginPoint, globalCartesian),
            actualMeshCode);
        return true;
    }

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

    private static Float3 CreateScenePosition(
        GeodeticPoint point,
        GeodeticPoint origin,
        LocalCartesian? cartesian)
    {
        return SceneAxisMapper.CreatePosition(
            point.Latitude,
            point.Longitude,
            point.Altitude,
            origin.Latitude,
            origin.Longitude,
            origin.Altitude,
            cartesian);
    }
}

internal readonly record struct DemTerrainObjectFrame(
    GeodeticPoint Origin,
    Float3 ScenePosition,
    string ThirdMeshCode);
