using GeographicLib;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class LocalCityGmlCommonMaterialEnumerator(
    IDefaultMaterialResolver materialResolver) : ICityGmlCommonMaterialEnumerator
{
    private readonly IDefaultMaterialResolver materialResolver = materialResolver;

    public IEnumerable<ResoniteMaterialBinding> Enumerate(
        CachedSourceFileDescriptor sourceFile,
        CoordinateReferenceSystem referenceSystem,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        PlateauImportRequest request,
        ISet<string>? emittedMaterialKeys = null)
    {
        return LocalCityGmlObjectProjection.EnumerateCommonMaterials(
            sourceFile.ToLegacy(),
            referenceSystem.ToLegacy(),
            globalOriginPoint.ToLegacy(),
            globalCartesian,
            demTerrainTextureOverlays,
            terrainHeightSampler: null,
            request,
            materialResolver,
            emittedMaterialKeys);
    }
}
