using PlateauResoniteLink.Application.Importing.Contracts;

namespace PlateauResoniteLink.Application.Importing;

internal interface IImportedSceneMetadataComposer
{
    ImportedSceneMetadata Compose(
        ResolvedLocalPlateauImportRequest request,
        ImportedSceneSourceSnapshot readResult);
}
