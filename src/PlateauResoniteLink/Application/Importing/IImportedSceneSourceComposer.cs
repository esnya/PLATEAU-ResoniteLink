using System;

namespace PlateauResoniteLink.Application.Importing;

internal interface IImportedSceneSourceComposer
{
    IImportedSceneSource Compose(
        ResolvedLocalPlateauImportRequest request,
        ImportedSceneSourceSnapshot readResult,
        ImportedObjectUnitOptimizer objectUnitOptimizer,
        Action<string>? progressReporter = null);
}
