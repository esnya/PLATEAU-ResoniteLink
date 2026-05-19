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

    public IEnumerable<ImportedCityObject> ProjectCityObjects(
        CachedSourceFileDescriptor sourceFile,
        CoordinateReferenceSystem referenceSystem,
        GeodeticPoint globalOriginPoint,
        LocalCartesian? globalCartesian,
        IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
        IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
        PlateauImportRequest request,
        Func<ParsedCityObject, bool>? predicate = null,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return LocalCityGmlObjectProjection.ProjectCityObjects(
            sourceFile,
            referenceSystem,
            globalOriginPoint,
            globalCartesian,
            demTerrainTextureOverlays,
            requestedMeshCodeBounds,
            request,
            materialResolver,
            predicate,
            progressReporter,
            cancellationToken);
    }
}
