using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal sealed class LocalCityGmlConstructionSourceFactory : IResoniteConstructionSourceFactory
{
    private readonly ICityGmlDocumentReader documentReader;
    private readonly IResoniteConstructionComposer constructionComposer;

    internal LocalCityGmlConstructionSourceFactory(
        ICityGmlDocumentReader documentReader,
        IResoniteConstructionComposer constructionComposer)
    {
        this.documentReader = documentReader;
        this.constructionComposer = constructionComposer;
    }

    public Task<IResoniteConstructionSource> CreateAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return CreateCoreAsync(request, progressReporter, cancellationToken);
    }

    private async Task<IResoniteConstructionSource> CreateCoreAsync(
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
}
