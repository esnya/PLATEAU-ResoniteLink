using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class DefaultImportedSceneSourceFactory : IImportedSceneSourceFactory
{
    private readonly ICityGmlDocumentReader documentReader;
    private readonly ImportedSceneSourceComposer constructionComposer;
    private readonly ImportedObjectUnitOptimizer objectUnitOptimizer;

    internal DefaultImportedSceneSourceFactory(
        ICityGmlDocumentReader documentReader,
        ImportedSceneSourceComposer constructionComposer,
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
        ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CreateResolvedCoreAsync(
            request,
            loggerFactory ?? NullLoggerFactory.Instance,
            cancellationToken);
    }

    private async Task<IImportedSceneSource> CreateResolvedCoreAsync(
        ResolvedLocalPlateauImportRequest request,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ImportedSceneSourceSnapshot readResult = await documentReader.ReadAsync(
            request,
            loggerFactory.CreateLogger("PlateauResoniteLink.Import"),
            cancellationToken);
        return constructionComposer(request, readResult, objectUnitOptimizer, loggerFactory);
    }
}
