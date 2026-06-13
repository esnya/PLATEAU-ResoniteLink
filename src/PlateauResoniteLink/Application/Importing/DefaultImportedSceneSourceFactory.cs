using System;
using System.Threading;
using System.Threading.Tasks;

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
        ArgumentNullException.ThrowIfNull(documentReader);
        ArgumentNullException.ThrowIfNull(constructionComposer);
        ArgumentNullException.ThrowIfNull(objectUnitOptimizer);
        this.documentReader = documentReader;
        this.constructionComposer = constructionComposer;
        this.objectUnitOptimizer = objectUnitOptimizer;
    }

    public Task<IImportedSceneSource> CreateAsync(
        ResolvedLocalPlateauImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CreateResolvedCoreAsync(request, cancellationToken);
    }

    private async Task<IImportedSceneSource> CreateResolvedCoreAsync(
        ResolvedLocalPlateauImportRequest request,
        CancellationToken cancellationToken)
    {
        ImportedSceneSourceSnapshot readResult = await documentReader.ReadAsync(request, cancellationToken);
        return constructionComposer.Compose(request, readResult, objectUnitOptimizer);
    }
}
