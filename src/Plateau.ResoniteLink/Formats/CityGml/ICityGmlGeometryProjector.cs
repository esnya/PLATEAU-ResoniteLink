using GeographicLib;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal interface ICityGmlGeometryProjector
{
    IEnumerable<ResoniteConstructionCityObject> MaterializeCityObjects(
        CachedSourceFileDescriptor sourceFile,
        CoordinateReferenceSystem referenceSystem,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<LocalCityGmlResonitePlanBuilder.MeshCodeArea> requestedMeshAreas,
        PlateauImportRequest request,
        Func<BootstrapParsedCityObject, bool>? predicate = null);
}
