using GeographicLib;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal interface ICityGmlGeometryProjector
{
    IEnumerable<ResoniteConstructionCityObject> MaterializeCityObjects(
        LocalCityGmlResonitePlanBuilder.CachedSourceFileDescriptor sourceFile,
        LocalCityGmlResonitePlanBuilder.CoordinateReferenceSystem referenceSystem,
        LocalCityGmlResonitePlanBuilder.GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        LocalCityGmlResonitePlanBuilder.TerrainHeightSampler? terrainHeightSampler,
        PlateauImportRequest request,
        Func<LocalCityGmlResonitePlanBuilder.ParsedCityObject, bool>? predicate = null);
}
