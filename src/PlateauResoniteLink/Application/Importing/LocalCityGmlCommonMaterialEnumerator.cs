using System.Collections.Generic;

using GeographicLib;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class LocalCityGmlCommonMaterialEnumerator(
    IDefaultMaterialResolver materialResolver) : ICityGmlCommonMaterialEnumerator
{
    private readonly IDefaultMaterialResolver materialResolver = materialResolver;

    public IEnumerable<MaterialBinding> Enumerate(
        CachedSourceFileDescriptor sourceFile,
        CoordinateReferenceSystem referenceSystem,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds>? requestedMeshAreas,
        PlateauImportRequest request,
        ISet<string>? emittedMaterialKeys = null)
    {
        return LocalCityGmlObjectProjection.EnumerateCommonMaterials(
            sourceFile.ToLegacy(),
            referenceSystem.ToLegacy(),
            globalOriginPoint.ToLegacy(),
            globalCartesian,
            demTerrainTextureOverlays,
            requestedMeshAreas,
            terrainHeightSampler: null,
            request,
            materialResolver,
            emittedMaterialKeys);
    }
}
