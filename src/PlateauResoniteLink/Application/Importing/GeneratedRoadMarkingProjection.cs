using System;

using GeographicLib;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal delegate ImportedCityObject CityObjectProjection(
    ParsedCityObject cityObject,
    GeodeticPoint globalOriginPoint,
    LocalCartesian? globalCartesian,
    TerrainTextureOverlay? demTerrainTextureOverlay,
    IDefaultMaterialResolver materialResolver);

internal static class GeneratedRoadMarkingProjection
{
    public static ImportedCityObject? TryProject(
        ParsedCityObject cityObject,
        TerrainTextureOverlay? demTerrainTextureOverlay,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IDefaultMaterialResolver materialResolver,
        CityObjectProjection projectCityObject)
    {
        ArgumentNullException.ThrowIfNull(cityObject);
        ArgumentNullException.ThrowIfNull(globalOriginPoint);
        ArgumentNullException.ThrowIfNull(materialResolver);
        ArgumentNullException.ThrowIfNull(projectCityObject);

        GeodeticPoint markingOrigin = CityObjectGeometryMetrics.GetCenterOrigin(cityObject);
        LocalCartesian? markingCartesian = cityObject.ReferenceSystem.IsGeographic
            ? new LocalCartesian(
                markingOrigin.Latitude,
                markingOrigin.Longitude,
                markingOrigin.Altitude,
                cityObject.ReferenceSystem.Geocentric)
            : null;
        ParsedCityObject? roadMarkingCityObject = GeneratedRoadMarkingFactory.TryCreate(
            cityObject,
            markingOrigin,
            markingCartesian);
        if (roadMarkingCityObject is null)
        {
            return null;
        }

        ImportedCityObject markingObject = projectCityObject(
            roadMarkingCityObject,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlay,
            materialResolver) with
        {
            CollisionEnabled = false,
        };
        return ImportedCityObjectGeometryPredicates.HasRenderableGeometry(markingObject)
            ? markingObject
            : null;
    }
}
