using System;

using Microsoft.Extensions.Logging;

namespace PlateauResoniteLink.Application.Importing;

internal delegate IImportedSceneSource ImportedSceneSourceComposer(
    ResolvedLocalPlateauImportRequest request,
    ImportedSceneSourceSnapshot readResult,
    ImportedObjectUnitOptimizer objectUnitOptimizer,
    ILoggerFactory? loggerFactory = null);
