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
        TerrainHeightSampler? terrainHeightSampler,
        PlateauImportRequest request,
        Func<BootstrapParsedCityObject, bool>? predicate = null)
    {
        return LocalCityGmlResonitePlanBuilder.MaterializeCityObjects(
            sourceFile.ToLegacy(),
            referenceSystem.ToLegacy(),
            globalOriginPoint.ToLegacy(),
            globalCartesian,
            demTerrainTextureOverlays,
            terrainHeightSampler?.ToLegacy(),
            request,
            materialResolver,
            predicate is null ? null : legacyCityObject => predicate(BootstrapParsedCityObject.FromLegacy(legacyCityObject)));
    }
}
