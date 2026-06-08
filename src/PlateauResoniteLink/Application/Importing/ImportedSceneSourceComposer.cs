using System;

namespace PlateauResoniteLink.Application.Importing;

internal delegate IImportedSceneSource ImportedSceneSourceComposer(
    ResolvedLocalPlateauImportRequest request,
    ImportedSceneSourceSnapshot readResult,
    ImportedObjectUnitOptimizer objectUnitOptimizer,
    Action<string>? progressReporter = null);
