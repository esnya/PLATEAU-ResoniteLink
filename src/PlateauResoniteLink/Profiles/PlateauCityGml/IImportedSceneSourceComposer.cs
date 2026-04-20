using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

public interface IImportedSceneSourceComposer
{
    IImportedSceneSource Compose(
        PlateauImportRequest request,
        LocalCityGmlDocumentReadResult readResult,
        Action<string>? progressReporter = null);
}
