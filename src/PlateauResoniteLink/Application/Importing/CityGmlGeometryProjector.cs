using System;
using System.Collections.Generic;
using System.Threading;

using GeographicLib;

using Microsoft.Extensions.Logging;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal delegate IEnumerable<ImportedCityObject> CityGmlGeometryProjector(
    CachedSourceFileDescriptor sourceFile,
    CoordinateReferenceSystem referenceSystem,
    GeodeticPoint globalOriginPoint,
    LocalCartesian? globalCartesian,
    IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
    IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
    IReadOnlyList<string> selectedMeshCodes,
    PlateauImportRequest request,
    Func<ParsedCityObject, bool>? predicate = null,
    ILogger? logger = null,
    CancellationToken cancellationToken = default);
