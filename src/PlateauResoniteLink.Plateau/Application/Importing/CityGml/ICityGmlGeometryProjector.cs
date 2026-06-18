using System;
using System.Collections.Generic;
using System.Threading;

using GeographicLib;


using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Application.Importing.Contracts;
using PlateauResoniteLink.Application.Importing.Plateau;
using PlateauResoniteLink.Application.Importing.Source;

namespace PlateauResoniteLink.Application.Importing.CityGml;

internal interface ICityGmlGeometryProjector
{
    IEnumerable<ImportedCityObject> ProjectCityObject(
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
        return ProjectCityObjects(
            new CachedSourceFileDescriptor(input.SourceFile, [input.CityObject], input.ReferenceSystem),
            referenceSystem,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlays,
            requestedMeshCodeBounds,
            selectedMeshCodes,
            request,
            predicate, cancellationToken);
    }

    IEnumerable<ImportedCityObject> ProjectCityObjects(
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
        foreach (ParsedCityObject cityObject in sourceFile.CityObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (ImportedCityObject projectedCityObject in ProjectCityObject(
                         new CityObjectProjectionInput(sourceFile.SourceFile, cityObject, sourceFile.ReferenceSystem),
                         referenceSystem,
                         globalOriginPoint,
                         globalCartesian,
                         demTerrainTextureOverlays,
                         requestedMeshCodeBounds,
                         selectedMeshCodes,
                         request,
                         demTerrainGridSamplingDraft: null,
                         predicate, cancellationToken))
            {
                yield return projectedCityObject;
            }
        }
    }
}
