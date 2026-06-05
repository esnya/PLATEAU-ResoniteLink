using System;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal interface IImportedSceneSourceComposer
{
    IImportedSceneSource Compose(
        PlateauImportRequest request,
        ImportedSceneSourceSnapshot readResult,
        IImportedObjectUnitOptimizer objectUnitOptimizer,
        Action<string>? progressReporter = null);
}
