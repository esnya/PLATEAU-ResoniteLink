using System;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal interface IImportedSceneSourceComposer
{
    IImportedSceneSource Compose(
        PlateauImportRequest request,
        ImportedSceneSourceSnapshot readResult,
        Action<string>? progressReporter = null,
        IImportedObjectUnitOptimizer? objectUnitOptimizer = null);
}
