using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal sealed class LocalCityGmlConstructionSourceFactory : IImportedSceneSourceFactory
{
    private readonly ICityGmlDocumentReader documentReader;
    private readonly IImportedSceneSourceComposer constructionComposer;

    internal LocalCityGmlConstructionSourceFactory(
        ICityGmlDocumentReader documentReader,
        IImportedSceneSourceComposer constructionComposer)
    {
        this.documentReader = documentReader;
        this.constructionComposer = constructionComposer;
    }

    public Task<IImportedSceneSource> CreateAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return CreateAsyncFromRequestCoreAsync(request, progressReporter, cancellationToken);
    }

    public Task<IImportedSceneSource> CreateAsync(
        PlateauImportRequest request,
        LocalCityGmlDocumentSet documentSet,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentSet);
        return CreateCoreAsync(request, documentSet, progressReporter, cancellationToken);
    }

    private async Task<IImportedSceneSource> CreateAsyncFromRequestCoreAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        LocalCityGmlDocumentSet documentSet = await documentReader.ReadAsync(
            request,
            progressReporter,
            cancellationToken);
        return constructionComposer.Compose(request, documentSet, progressReporter);
    }

    private Task<IImportedSceneSource> CreateCoreAsync(
        PlateauImportRequest request,
        LocalCityGmlDocumentSet documentSet,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(constructionComposer.Compose(request, documentSet, progressReporter));
    }
}
