using System;
using System.Collections.Generic;
using System.Linq;

using GeographicLib;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class LocalCityGmlGeometryProjector(
    IDefaultMaterialResolver materialResolver) : ICityGmlGeometryProjector
{
    private readonly IDefaultMaterialResolver materialResolver = materialResolver;

    public IEnumerable<ImportedCityObject> ProjectCityObjects(
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
                predicate is null ? null : cityObject => predicate(BootstrapParsedCityObject.FromLegacy(cityObject)))
            .Select(SceneImportContractMapper.ToContract);
    }
}
