using Microsoft.Extensions.Logging;

namespace PlateauResoniteLink.Application.Importing;

internal interface IImportedSceneSourceComposer
{
    IImportedSceneSource Compose(
        ResolvedLocalPlateauImportRequest request,
        ImportedSceneSourceSnapshot readResult,
        ImportedObjectUnitOptimizer objectUnitOptimizer,
        ILoggerFactory? loggerFactory = null);
}
