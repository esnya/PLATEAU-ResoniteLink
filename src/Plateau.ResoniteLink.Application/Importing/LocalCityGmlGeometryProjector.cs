using GeographicLib;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal sealed class LocalCityGmlGeometryProjector(IDefaultMaterialResolver materialResolver) : ICityGmlGeometryProjector
{
    private readonly IDefaultMaterialResolver materialResolver = materialResolver;

    public IEnumerable<ResoniteConstructionCityObject> MaterializeCityObjects(
        LocalCityGmlResonitePlanBuilder.CachedSourceFileDescriptor sourceFile,
        LocalCityGmlResonitePlanBuilder.CoordinateReferenceSystem referenceSystem,
        LocalCityGmlResonitePlanBuilder.GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        LocalCityGmlResonitePlanBuilder.TerrainHeightSampler? terrainHeightSampler,
        PlateauImportRequest request,
        Func<LocalCityGmlResonitePlanBuilder.ParsedCityObject, bool>? predicate = null)
    {
        return LocalCityGmlResonitePlanBuilder.MaterializeCityObjects(
            sourceFile,
            referenceSystem,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlays,
            terrainHeightSampler,
            request,
            materialResolver,
            predicate);
    }
}
