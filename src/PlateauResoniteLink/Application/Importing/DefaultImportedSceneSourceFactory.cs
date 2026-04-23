using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class DefaultImportedSceneSourceFactory : IImportedSceneSourceFactory
{
    private readonly ICityGmlDocumentReader documentReader;
    private readonly IImportedSceneSourceComposer constructionComposer;
    private readonly IDemTextureSourcePolicy demTextureSourcePolicy;
    private readonly IImportedObjectUnitOptimizer objectUnitOptimizer;

    internal DefaultImportedSceneSourceFactory(
        ICityGmlDocumentReader documentReader,
        IImportedSceneSourceComposer constructionComposer,
        IDemTextureSourcePolicy demTextureSourcePolicy,
        IImportedObjectUnitOptimizer? objectUnitOptimizer = null)
    {
        this.documentReader = documentReader;
        this.constructionComposer = constructionComposer;
        this.demTextureSourcePolicy = demTextureSourcePolicy;
        this.objectUnitOptimizer = objectUnitOptimizer ?? new PassthroughImportedObjectUnitOptimizer();
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
        await ValidateDemTextureSourceAsync(request, readResult, cancellationToken);
        return await Task.FromResult(constructionComposer.Compose(request, readResult, progressReporter, objectUnitOptimizer));
    }

    private async Task ValidateDemTextureSourceAsync(
        PlateauImportRequest request,
        ImportedSceneSourceSnapshot readResult,
        CancellationToken cancellationToken)
    {
        if (request.DemTextureSource is null
            || !readResult.DocumentSet.PackageNames.Contains("dem", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        IReadOnlyList<DemTerrainOverlayRegion> overlayRegions = await DemOverlayRegionResolver.ResolveAsync(
            readResult.BootstrapContext,
            readResult.DocumentSet.SelectedMeshCodes,
            cancellationToken);

        if (overlayRegions.Count == 0)
        {
            return;
        }

        _ = await demTextureSourcePolicy.ResolveAsync(
            request,
            overlayRegions,
            cancellationToken);
    }
}
