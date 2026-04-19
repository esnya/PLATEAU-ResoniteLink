using GeographicLib;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal sealed class LocalCityGmlCommonMaterialEnumerator(IDefaultMaterialResolver materialResolver)
    : ICityGmlCommonMaterialEnumerator
{
    private readonly IDefaultMaterialResolver materialResolver = materialResolver;

    public IEnumerable<ResoniteMaterialBinding> EnumerateCommonMaterials(
        CachedSourceFileDescriptor sourceFile,
        CoordinateReferenceSystem referenceSystem,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        PlateauImportRequest request,
        ISet<string> emittedMaterialKeys)
    {
        ArgumentNullException.ThrowIfNull(emittedMaterialKeys);

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
