using System;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing.Contracts;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class DefaultImportedSceneSourceFactory : IImportedSceneSourceFactory
{
    private readonly IResolvedPlateauSceneSourceReader sourceReader;
    private readonly IImportedSceneSourceComposer constructionComposer;
    private readonly IImportedObjectUnitOptimizer objectUnitOptimizer;

    internal DefaultImportedSceneSourceFactory(
        IResolvedPlateauSceneSourceReader sourceReader,
        IImportedSceneSourceComposer constructionComposer,
        IImportedObjectUnitOptimizer objectUnitOptimizer)
    {
        ArgumentNullException.ThrowIfNull(sourceReader);
        ArgumentNullException.ThrowIfNull(constructionComposer);
        ArgumentNullException.ThrowIfNull(objectUnitOptimizer);
        this.sourceReader = sourceReader;
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
        ImportedSceneSourceSnapshot readResult = await sourceReader.ReadAsync(request, cancellationToken);
        return constructionComposer.Compose(request, readResult, objectUnitOptimizer);
    }
}
