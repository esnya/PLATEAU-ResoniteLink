using System;
using System.Collections.Generic;
using System.Linq;

using GeographicLib;

namespace PlateauResoniteLink.Application.Importing;

internal sealed record ProjectedCityObjectContext(
    ParsedCityObject CityObject,
    GeodeticPoint Origin,
    LocalCartesian? Cartesian,
    Float3 SlotPosition,
    double MinimumAltitude,
    HashSet<string> CulledSurfaceIds,
    FacadeUvProjectionContext? FacadeUvProjectionContext);

internal static class ProjectedCityObjectContextFactory
{
    public static ProjectedCityObjectContext Create(
        ParsedCityObject cityObject,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian)
    {
        ArgumentNullException.ThrowIfNull(cityObject);
        ArgumentNullException.ThrowIfNull(globalOriginPoint);

        GeodeticPoint cityObjectOrigin = CityObjectGeometryMetrics.GetCenterOrigin(cityObject);
        LocalCartesian? cityObjectCartesian = cityObject.ReferenceSystem.IsGeographic
            ? new LocalCartesian(
                cityObjectOrigin.Latitude,
                cityObjectOrigin.Longitude,
                cityObjectOrigin.Altitude,
                cityObject.ReferenceSystem.Geocentric)
            : null;
        Float3 slotPosition = SceneAxisMapper.CreatePosition(
            cityObjectOrigin.Latitude,
            cityObjectOrigin.Longitude,
            cityObjectOrigin.Altitude,
            globalOriginPoint.Latitude,
            globalOriginPoint.Longitude,
            globalOriginPoint.Altitude,
            globalCartesian);
        HashSet<string> culledSurfaceIds = BottomBandSurfaceCuller.GetCulledSurfaceIds(
            cityObject.PackageName,
            cityObject.Surfaces,
            cityObjectOrigin,
            cityObjectCartesian);
        FacadeUvProjectionContext? facadeUvProjectionContext = FacadeUvProjectionContextFactory.TryCreate(
            cityObject.PackageName,
            cityObject.Surfaces,
            cityObjectOrigin,
            cityObjectCartesian);

        return new ProjectedCityObjectContext(
            cityObject,
            cityObjectOrigin,
            cityObjectCartesian,
            slotPosition,
            cityObject.Surfaces
                .SelectMany(static surface => surface.Vertices)
                .Min(static vertex => vertex.Altitude),
            culledSurfaceIds,
            facadeUvProjectionContext);
    }
}
