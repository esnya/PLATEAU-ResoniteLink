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
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return CreateAsyncFromRequestCoreAsync(request, progressReporter, cancellationToken);
    }

    public Task<IImportedSceneSource> CreateAsync(
        PlateauImportRequest request,
        LocalCityGmlDocumentReadResult readResult,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readResult);
        return CreateCoreAsync(request, readResult, progressReporter, cancellationToken);
    }

    private async Task<IImportedSceneSource> CreateAsyncFromRequestCoreAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        LocalCityGmlDocumentReadResult readResult = await documentReader.ReadAsync(
            request,
            progressReporter,
            cancellationToken);
        return await CreateResolvedCoreAsync(request, readResult, progressReporter, cancellationToken);
    }

    private Task<IImportedSceneSource> CreateCoreAsync(
        PlateauImportRequest request,
        LocalCityGmlDocumentReadResult readResult,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
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
        IReadOnlyList<DemTerrainOverlayRegion> requestedOverlayRegions = await ResolveRequestedDemOverlayRegionsAsync(
            readResult,
            requestedDemMeshCodes,
            cancellationToken);
        ResolvedDemTextureSources resolvedDemTextureSources = await demTextureSourcePolicy.ResolveAsync(
            request,
            requestedOverlayRegions,
            cancellationToken);
        if (resolvedDemTextureSources.Overlays.Count == 0)
        {
            return readResult;
        }

        LocalCityGmlDocumentSet resolvedDocumentSet = new(
            documentSet.DatasetSource,
            documentSet.RelativeSourceFiles,
            documentSet.PackageNames,
            resolvedDemTextureSources.Overlays,
            documentSet.SelectedMeshCodes);
        LocalCityGmlBootstrapContext resolvedBootstrapContext = new(
            readResult.BootstrapContext.SourceFilePipelines,
            readResult.BootstrapContext.GlobalOriginPoint);
        return new LocalCityGmlDocumentReadResult(
            resolvedDocumentSet,
            resolvedBootstrapContext);
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

    private static async Task<IReadOnlyList<DemTerrainOverlayRegion>> ResolveRequestedDemOverlayRegionsAsync(
        LocalCityGmlDocumentReadResult readResult,
        IReadOnlyList<string> requestedDemMeshCodes,
        CancellationToken cancellationToken)
    {
        SourceFilePipeline[] demPipelines = readResult.BootstrapContext.SourceFilePipelines
            .Where(static pipeline => string.Equals(
                pipeline.SourceFile.PackageName,
                "dem",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (demPipelines.Length == 0)
        {
            return LocalCityGmlDemBootstrapSupport.CreateDemTerrainOverlayRegions(requestedDemMeshCodes);
        }

        ParsedSourceFileResult[] parsedDemSourceFiles = await Task.WhenAll(
            demPipelines.Select(pipeline => pipeline.GetParseTask().WaitAsync(cancellationToken)));
        DemTerrainBounds? demBounds = LocalCityGmlDemBootstrapSupport.ResolveDemTerrainBounds(
            parsedDemSourceFiles,
            fallbackBounds: null);
        return demBounds is null
            ? LocalCityGmlDemBootstrapSupport.CreateDemTerrainOverlayRegions(requestedDemMeshCodes)
            : LocalCityGmlDemBootstrapSupport.CreateDemTerrainOverlayRegions(demBounds, requestedDemMeshCodes);
    }
}
