using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

internal sealed class LocalCityGmlConstructionSourceFactory : IResoniteConstructionSourceFactory
{
    private readonly ICityGmlDocumentReader documentReader;
    private readonly IResoniteConstructionComposer constructionComposer;
    private readonly IPlateauDatasetContentSourceFactory datasetContentSourceFactory;

    internal LocalCityGmlConstructionSourceFactory(
        ICityGmlDocumentReader documentReader,
        IResoniteConstructionComposer constructionComposer,
        IPlateauDatasetContentSourceFactory? datasetContentSourceFactory = null)
    {
        this.documentReader = documentReader;
        this.constructionComposer = constructionComposer;
        this.datasetContentSourceFactory = datasetContentSourceFactory
            ?? new DefaultPlateauDatasetContentSourceFactory(
                new RemoteArchiveDistributionPolicy(),
                new ArchiveFileLayoutPolicy());
    }

    public Task<IResoniteConstructionSource> CreateAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return CreateAsyncFromRequestCoreAsync(request, progressReporter, cancellationToken);
    }

    public Task<IResoniteConstructionSource> CreateAsync(
        PlateauImportRequest request,
        LocalCityGmlDocumentSet documentSet,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentSet);
        return CreateCoreAsync(request, documentSet, progressReporter, cancellationToken);
    }

    private async Task<IResoniteConstructionSource> CreateAsyncFromRequestCoreAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        LocalCityGmlDocumentSet documentSet = await documentReader.ReadAsync(
            request,
            progressReporter,
            cancellationToken);
        return await CreateCoreAsync(request, documentSet, progressReporter, cancellationToken);
    }

    private async Task<IResoniteConstructionSource> CreateCoreAsync(
        PlateauImportRequest request,
        LocalCityGmlDocumentSet documentSet,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LocalCityGmlDocumentSet resolvedDocumentSet = await ResolveDocumentSetAsync(
            request,
            documentSet,
            cancellationToken);
        return constructionComposer.Compose(request, resolvedDocumentSet, progressReporter);
    }

    private async Task<LocalCityGmlDocumentSet> ResolveDocumentSetAsync(
        PlateauImportRequest request,
        LocalCityGmlDocumentSet documentSet,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(documentSet);

        if (documentSet.TerrainTextureOverlays.Count > 0
            || !documentSet.PackageNames.Contains("dem", StringComparer.Ordinal))
        {
            return documentSet;
        }

        (TerrainTextureOverlay[] overlays, DemTerrainGeoReferencedRasterCatalog? demRasterCatalog) = await CreateDemTerrainTextureOverlaysAsync(
            request,
            documentSet.RequestedMeshCodes.Count > 0
                ? documentSet.RequestedMeshCodes
                : [request.MeshCode],
            cancellationToken);
        if (overlays.Length == 0)
        {
            return documentSet;
        }

        return new LocalCityGmlDocumentSet(
            documentSet.DatasetSource,
            documentSet.RelativeSourceFiles,
            documentSet.PackageNames,
            overlays,
            documentSet.RequestedMeshCodes,
            documentSet.BootstrapSourceFilePipelines,
            documentSet.BootstrapCachedDemSourceFiles,
            documentSet.BootstrapReferenceSystem,
            documentSet.BootstrapGlobalOriginPoint,
            documentSet.BootstrapTerrainHeightSampler,
            demRasterCatalog);
    }

    private async Task<(TerrainTextureOverlay[] Overlays, DemTerrainGeoReferencedRasterCatalog? RasterCatalog)> CreateDemTerrainTextureOverlaysAsync(
        PlateauImportRequest request,
        IReadOnlyList<string> requestedMeshCodes,
        CancellationToken cancellationToken)
    {
        DemTerrainGeoReferencedRasterCatalog? demRasterCatalog = await DemTerrainGeoReferencedRasterCatalog.CreateAsync(
            request.DemTextureSource,
            datasetContentSourceFactory,
            cancellationToken);
        if (request.DemTextureSource is not null && demRasterCatalog is null)
        {
            throw new PlateauImportValidationException(
                [LocalCityGmlImportErrorMessages.InvalidDemTextureSource(request.DemTextureSource)]);
        }

        TerrainTextureOverlay[] overlays = await LocalCityGmlDemBootstrapSupport.CreateDemTerrainTextureOverlaysAsync(
            requestedMeshCodes,
            demRasterCatalog,
            cancellationToken);
        if (request.DemTextureSource is not null
            && overlays.Any(static overlay => !overlay.EnumerateGeoReferencedRasterSources().Any()))
        {
            throw new PlateauImportValidationException(
                [LocalCityGmlImportErrorMessages.InvalidDemTextureSource(request.DemTextureSource)]);
        }

        return (overlays, demRasterCatalog);
    }
}
