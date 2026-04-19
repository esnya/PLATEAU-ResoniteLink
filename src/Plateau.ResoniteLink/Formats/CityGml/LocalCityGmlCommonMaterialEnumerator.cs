using GeographicLib;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal sealed class LocalCityGmlCommonMaterialEnumerator(
    IDefaultMaterialResolver materialResolver,
    ICityGmlLegacyProjectionBridge? legacyProjectionBridge = null) : ICityGmlCommonMaterialEnumerator
{
    private readonly IDefaultMaterialResolver materialResolver = materialResolver;
    private readonly ICityGmlLegacyProjectionBridge legacyProjectionBridge = legacyProjectionBridge ?? new LocalCityGmlLegacyProjectionBridge();

    public IEnumerable<ResoniteMaterialBinding> Enumerate(
        CachedSourceFileDescriptor sourceFile,
        CoordinateReferenceSystem referenceSystem,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        PlateauImportRequest request,
        ISet<string>? emittedMaterialKeys = null)
    {
        return legacyProjectionBridge.EnumerateCommonMaterials(
            sourceFile,
            referenceSystem,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlays,
            terrainHeightSampler: null,
            request,
            materialResolver,
            emittedMaterialKeys);
    }
}
