using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

public interface IImportedSceneSourceFactory
{
    Task<IImportedSceneSource> CreateAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default);

    Task<IImportedSceneSource> CreateAsync(
        PlateauImportRequest request,
        LocalCityGmlDocumentReadResult readResult,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return CreateAsync(request, progressReporter, cancellationToken);
    }
}
