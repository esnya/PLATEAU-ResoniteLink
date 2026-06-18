using PlateauResoniteLink.Core.Application.Importing;
using PlateauResoniteLink.Core.Application.Importing.Contracts;

namespace PlateauResoniteLink.Plateau.Application.Importing;

internal interface IImportedSceneMetadataComposer
{
    ImportedSceneMetadata Compose(
        ResolvedLocalPlateauImportRequest request,
        ImportedSceneSourceSnapshot readResult);
}
