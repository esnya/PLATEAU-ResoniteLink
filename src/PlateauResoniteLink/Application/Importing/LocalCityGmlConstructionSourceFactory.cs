using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class LocalCityGmlConstructionSourceFactory : IImportedSceneSourceFactory
{
    private readonly ICityGmlDocumentReader documentReader;
    private readonly IImportedSceneSourceComposer constructionComposer;
    private readonly IDemTextureSourcePolicy demTextureSourcePolicy;

    internal LocalCityGmlConstructionSourceFactory(
        ICityGmlDocumentReader documentReader,
        IImportedSceneSourceComposer constructionComposer,
        IDemTextureSourcePolicy demTextureSourcePolicy)
    {
        this.documentReader = documentReader;
        this.constructionComposer = constructionComposer;
        this.demTextureSourcePolicy = demTextureSourcePolicy;
    }

    public Task<IImportedSceneSource> CreateAsync(
        PlateauImportRequest request,
        LocalCityGmlDocumentReadResult readResult,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(readResult);
        return CreateResolvedCoreAsync(request, readResult, progressReporter, cancellationToken);
    }

    private async Task<IImportedSceneSource> CreateResolvedCoreAsync(
        PlateauImportRequest request,
        LocalCityGmlDocumentReadResult readResult,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        await ValidateDemTextureSourceAsync(request, readResult, cancellationToken);
        return await Task.FromResult(constructionComposer.Compose(request, readResult, progressReporter));
    }

    private async Task ValidateDemTextureSourceAsync(
        PlateauImportRequest request,
        LocalCityGmlDocumentReadResult readResult,
        CancellationToken cancellationToken)
    {
        if (request.DemTextureSource is null
            || !readResult.DocumentSet.PackageNames.Contains("dem", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        IReadOnlyList<DemTerrainOverlayRegion> overlayRegions = await readResult.ResolveRequestedDemOverlayRegionsAsync(
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
