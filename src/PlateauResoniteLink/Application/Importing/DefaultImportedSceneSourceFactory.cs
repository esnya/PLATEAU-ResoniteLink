using System;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class DefaultImportedSceneSourceFactory : IImportedSceneSourceFactory
{
    private readonly ICityGmlDocumentReader documentReader;
    private readonly IImportedSceneSourceComposer constructionComposer;
    private readonly ImportedObjectUnitOptimizer objectUnitOptimizer;

    internal DefaultImportedSceneSourceFactory(
        ICityGmlDocumentReader documentReader,
        IImportedSceneSourceComposer constructionComposer,
        ImportedObjectUnitOptimizer objectUnitOptimizer)
    {
        ArgumentNullException.ThrowIfNull(documentReader);
        ArgumentNullException.ThrowIfNull(constructionComposer);
        ArgumentNullException.ThrowIfNull(objectUnitOptimizer);
        this.documentReader = documentReader;
        this.constructionComposer = constructionComposer;
        this.objectUnitOptimizer = objectUnitOptimizer;
    }

    public Task<IImportedSceneSource> CreateAsync(
        ResolvedLocalPlateauImportRequest request,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CreateResolvedCoreAsync(request, progressReporter, cancellationToken);
    }

    private async Task<IImportedSceneSource> CreateResolvedCoreAsync(
        ResolvedLocalPlateauImportRequest request,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        ImportedSceneSourceSnapshot readResult = await documentReader.ReadAsync(
            request,
            progressReporter,
            cancellationToken);
        return constructionComposer.Compose(request, readResult, objectUnitOptimizer, progressReporter);
    }
}
