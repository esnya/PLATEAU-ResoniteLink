namespace PlateauResoniteLink.Application.Importing;

internal interface IImportedSceneSourceComposer
{
    IImportedSceneSource Compose(
        ResolvedLocalPlateauImportRequest request,
        ImportedSceneSourceSnapshot readResult,
        IImportedObjectUnitOptimizer objectUnitOptimizer);
}
