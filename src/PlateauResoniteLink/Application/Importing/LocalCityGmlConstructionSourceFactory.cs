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
    private readonly IImportedCityObjectOptimizer cityObjectOptimizer;

    internal LocalCityGmlConstructionSourceFactory(
        ICityGmlDocumentReader documentReader,
        IImportedSceneSourceComposer constructionComposer,
        IDemTextureSourcePolicy demTextureSourcePolicy,
        IImportedCityObjectOptimizer cityObjectOptimizer)
    {
        this.documentReader = documentReader;
        this.constructionComposer = constructionComposer;
        this.demTextureSourcePolicy = demTextureSourcePolicy;
        this.cityObjectOptimizer = cityObjectOptimizer;
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
        LocalCityGmlBootstrapSnapshot readResult = await documentReader.ReadAsync(
            request,
            progressReporter,
            cancellationToken);
        await ValidateDemTextureSourceAsync(request, readResult, cancellationToken);
        return await Task.FromResult(constructionComposer.Compose(request, readResult, progressReporter, cityObjectOptimizer));
    }

    private async Task ValidateDemTextureSourceAsync(
        PlateauImportRequest request,
        LocalCityGmlBootstrapSnapshot readResult,
        CancellationToken cancellationToken)
    {
        if (request.DemTextureSource is null
            || !readResult.DocumentSet.PackageNames.Contains("dem", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        IReadOnlyList<DemTerrainOverlayRegion> overlayRegions = await LocalCityGmlDemOverlayRegionResolver.ResolveAsync(
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
