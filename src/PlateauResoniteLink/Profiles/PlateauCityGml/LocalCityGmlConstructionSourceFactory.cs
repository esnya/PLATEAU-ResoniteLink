using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class LocalCityGmlConstructionSourceFactory : IImportedSceneSourceFactory
{
    private readonly ICityGmlDocumentReader documentReader;
    private readonly IImportedSceneSourceComposer constructionComposer;
    private readonly IPlateauDatasetContentSourceFactory datasetContentSourceFactory;

    internal LocalCityGmlConstructionSourceFactory(
        ICityGmlDocumentReader documentReader,
        IImportedSceneSourceComposer constructionComposer,
        IPlateauDatasetContentSourceFactory? datasetContentSourceFactory = null)
    {
        this.documentReader = documentReader;
        this.constructionComposer = constructionComposer;
        this.datasetContentSourceFactory = datasetContentSourceFactory
            ?? new DefaultPlateauDatasetContentSourceFactory(
                new RemoteArchiveDistributionPolicy(),
                new ArchiveFileLayoutPolicy());
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

        (TerrainTextureOverlay[] overlays, DemTerrainGeoReferencedRasterCatalog? demRasterCatalog) = await CreateDemTerrainTextureOverlaysAsync(
            request,
            documentSet.RequestedMeshCodes.Count > 0
                ? documentSet.RequestedMeshCodes
                : [request.MeshCode],
            cancellationToken);
        if (overlays.Length == 0)
        {
            return readResult;
        }

        LocalCityGmlDocumentSet resolvedDocumentSet = new(
            documentSet.DatasetSource,
            documentSet.RelativeSourceFiles,
            documentSet.PackageNames,
            overlays,
            documentSet.RequestedMeshCodes);
        LocalCityGmlBootstrapContext resolvedBootstrapContext = new(
            readResult.BootstrapContext.SourceFilePipelines,
            readResult.BootstrapContext.GlobalOriginPoint,
            demRasterCatalog);
        return new LocalCityGmlDocumentReadResult(
            resolvedDocumentSet,
            resolvedBootstrapContext);
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
