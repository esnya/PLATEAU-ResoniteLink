using GeographicLib;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal sealed class LocalCityGmlGeometryProjector(IDefaultMaterialResolver materialResolver) : ICityGmlGeometryProjector
{
    private readonly IDefaultMaterialResolver materialResolver = materialResolver;

    public IEnumerable<ResoniteConstructionCityObject> MaterializeCityObjects(
        CachedSourceFileDescriptor sourceFile,
        CoordinateReferenceSystem referenceSystem,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<LocalCityGmlResonitePlanBuilder.MeshCodeArea> requestedMeshAreas,
        PlateauImportRequest request,
        Func<BootstrapParsedCityObject, bool>? predicate = null)
    {
        return LocalCityGmlResonitePlanBuilder.MaterializeCityObjects(
            sourceFile.ToLegacy(),
            referenceSystem.ToLegacy(),
            globalOriginPoint.ToLegacy(),
            globalCartesian,
            demTerrainTextureOverlays,
            requestedMeshAreas,
            terrainHeightSampler: null,
            request,
            materialResolver,
            predicate is null ? null : legacyCityObject => predicate(BootstrapParsedCityObject.FromLegacy(legacyCityObject)));
    }
}
