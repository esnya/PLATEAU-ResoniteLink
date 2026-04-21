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
        LocalCityGmlDocumentReadResult resolvedReadResult = await ResolveReadResultAsync(
            request,
            readResult,
            cancellationToken);
        return constructionComposer.Compose(request, resolvedReadResult, progressReporter);
    }

    private async Task<LocalCityGmlDocumentReadResult> ResolveReadResultAsync(
        PlateauImportRequest request,
        LocalCityGmlDocumentReadResult readResult,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(readResult);

        LocalCityGmlDocumentSet documentSet = readResult.DocumentSet;
        if (documentSet.TerrainTextureOverlays.Count > 0
            || !documentSet.PackageNames.Contains("dem", StringComparer.Ordinal))
        {
            return readResult;
        }

        IReadOnlyList<string> requestedDemMeshCodes = ResolveRequestedDemMeshCodes(request, documentSet);
        IReadOnlyList<DemTerrainOverlayRegion> requestedOverlayRegions = await readResult
            .ResolveRequestedDemOverlayRegionsAsync(requestedDemMeshCodes, cancellationToken);
        ResolvedDemTextureSources resolvedDemTextureSources = await demTextureSourcePolicy.ResolveAsync(
            request,
            requestedOverlayRegions,
            cancellationToken);
        if (resolvedDemTextureSources.Overlays.Count == 0)
        {
            return readResult;
        }

        LocalCityGmlDocumentSet resolvedDocumentSet = documentSet.WithTerrainTextureOverlays(
            resolvedDemTextureSources.Overlays);
        return readResult.WithDocumentSet(resolvedDocumentSet);
    }

    private static IReadOnlyList<string> ResolveRequestedDemMeshCodes(
        PlateauImportRequest request,
        LocalCityGmlDocumentSet documentSet)
    {
        LocalCityGmlSourceFileDiscoveryResult demDiscovery = LocalCityGmlSourceFileDiscovery.Discover(
            documentSet.RelativeSourceFiles,
            request.MeshCode,
            ["dem"]);
        return demDiscovery.SelectedMeshCodes.Count > 0
            ? demDiscovery.SelectedMeshCodes
            : [request.MeshCode];
    }
}
