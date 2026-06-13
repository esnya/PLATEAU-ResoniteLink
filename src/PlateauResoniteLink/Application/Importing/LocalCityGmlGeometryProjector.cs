using System;
using System.Collections.Generic;
using System.Threading;

using GeographicLib;


using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class LocalCityGmlGeometryProjector(
    IDefaultMaterialResolver materialResolver) : ICityGmlGeometryProjector
{
    private readonly IDefaultMaterialResolver materialResolver = materialResolver;

    public IEnumerable<ImportedCityObject> ProjectCityObject(
        CityObjectProjectionInput input,
        CoordinateReferenceSystem referenceSystem,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
        IReadOnlyList<string> selectedMeshCodes,
        PlateauImportRequest request,
        DemSourceFileTerrainGridSamplingDraft? demTerrainGridSamplingDraft = null,
        Func<ParsedCityObject, bool>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        return LocalCityGmlObjectProjection.ProjectCityObject(
            input,
            referenceSystem,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlays,
            requestedMeshCodeBounds,
            selectedMeshCodes,
            request,
            materialResolver,
            demTerrainGridSamplingDraft,
            predicate, cancellationToken);
    }

    public IEnumerable<ImportedCityObject> ProjectCityObjects(
        CachedSourceFileDescriptor sourceFile,
        CoordinateReferenceSystem referenceSystem,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
        IReadOnlyList<string> selectedMeshCodes,
        PlateauImportRequest request,
        Func<ParsedCityObject, bool>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        return LocalCityGmlObjectProjection.ProjectCityObjects(
            sourceFile,
            referenceSystem,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlays,
            requestedMeshCodeBounds,
            selectedMeshCodes,
            request,
            materialResolver,
            predicate, cancellationToken);
    }
}
