using System;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class DefaultImportedSceneSourceFactory : IImportedSceneSourceFactory
{
    private readonly ICityGmlDocumentReader documentReader;
    private readonly IImportedSceneSourceComposer constructionComposer;
    private readonly IImportedObjectUnitOptimizer objectUnitOptimizer;

    internal DefaultImportedSceneSourceFactory(
        ICityGmlDocumentReader documentReader,
        IImportedSceneSourceComposer constructionComposer,
        IImportedObjectUnitOptimizer objectUnitOptimizer)
    {
        ArgumentNullException.ThrowIfNull(objectUnitOptimizer);
        this.documentReader = documentReader;
        this.constructionComposer = constructionComposer;
        this.objectUnitOptimizer = objectUnitOptimizer;
    }

    public Task<IImportedSceneSource> CreateAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CreateResolvedCoreAsync(request, progressReporter, cancellationToken);
    }

    private async Task<IImportedSceneSource> CreateResolvedCoreAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        ImportedSceneSourceSnapshot readResult = await documentReader.ReadAsync(
            request,
            progressReporter,
            cancellationToken);
        return await Task.FromResult(constructionComposer.Compose(request, readResult, objectUnitOptimizer, progressReporter));
    }
}
