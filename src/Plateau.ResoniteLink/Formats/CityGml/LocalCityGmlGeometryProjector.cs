using GeographicLib;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal sealed class LocalCityGmlGeometryProjector(
    IDefaultMaterialResolver materialResolver) : ICityGmlGeometryProjector
{
    private readonly IDefaultMaterialResolver materialResolver = materialResolver;

    public IEnumerable<ResoniteConstructionCityObject> ProjectCityObjects(
        CachedSourceFileDescriptor sourceFile,
        CoordinateReferenceSystem referenceSystem,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds> requestedMeshAreas,
        PlateauImportRequest request,
        Func<BootstrapParsedCityObject, bool>? predicate = null)
    {
        return LocalCityGmlObjectProjection.ProjectCityObjects(
            sourceFile.ToLegacy(),
            referenceSystem.ToLegacy(),
            globalOriginPoint.ToLegacy(),
            globalCartesian,
            demTerrainTextureOverlays,
            requestedMeshAreas,
            terrainHeightSampler: null,
            request,
            materialResolver,
            predicate is null ? null : cityObject => predicate(BootstrapParsedCityObject.FromLegacy(cityObject)));
    }
}
