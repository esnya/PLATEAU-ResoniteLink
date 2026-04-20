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

        SourceFileDescriptor[] demSourceFiles = documentSet.BootstrapSourceFilePipelines
            .Where(static pipeline => string.Equals(pipeline.SourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase))
            .Select(static pipeline => pipeline.SourceFile)
            .ToArray();
        string[] demRequestedMeshCodes = documentSet.RequestedMeshCodes
            .Where(requestedMeshCode => demSourceFiles.Any(sourceFile => MatchesDemRequestedMeshCode(sourceFile, requestedMeshCode)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static meshCode => meshCode, StringComparer.Ordinal)
            .ToArray();

        (TerrainTextureOverlay[] overlays, DemTerrainGeoReferencedRasterCatalog? demRasterCatalog) = await CreateDemTerrainTextureOverlaysAsync(
            request,
            documentSet,
            demSourceFiles,
            demRequestedMeshCodes.Length > 0
                ? demRequestedMeshCodes
                : documentSet.RequestedMeshCodes.Count > 0
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
        LocalCityGmlDocumentSet documentSet,
        IReadOnlyList<SourceFileDescriptor> demSourceFiles,
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

        TerrainTextureOverlay[] overlays = request.DemTextureSource is null
            ? await LocalCityGmlDemBootstrapSupport.CreateDemTerrainTextureOverlaysAsync(
                requestedMeshCodes,
                demRasterCatalog,
                cancellationToken)
            : await CreateExplicitDemTerrainTextureOverlaysAsync(
                documentSet,
                demSourceFiles,
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

    internal static async Task<TerrainTextureOverlay[]> CreateExplicitDemTerrainTextureOverlaysAsync(
        LocalCityGmlDocumentSet documentSet,
        IReadOnlyList<SourceFileDescriptor> demSourceFiles,
        IReadOnlyList<string> requestedMeshCodes,
        DemTerrainGeoReferencedRasterCatalog? demRasterCatalog,
        CancellationToken cancellationToken)
    {
        TerrainTextureOverlay[] requestScopedFallbackOverlays = await LocalCityGmlDemBootstrapSupport.CreateDemTerrainTextureOverlaysAsync(
            requestedMeshCodes,
            demRasterCatalog,
            cancellationToken);
        if (demSourceFiles.Count == 0)
        {
            return requestScopedFallbackOverlays;
        }

        List<TerrainTextureOverlay> geometryScopedOverlays = [];
        foreach (SourceFilePipeline pipeline in documentSet.BootstrapSourceFilePipelines
                     .Where(pipeline => demSourceFiles.Contains(pipeline.SourceFile)))
        {
            BootstrapParsedCityObject[] parsedCityObjects = await pipeline.StreamParsedCityObjectsAsync(cancellationToken)
                .ToArrayAsync(cancellationToken);
            DemTerrainBounds? demBounds = LocalCityGmlDemBootstrapSupport.ResolveDemTerrainBounds(
                [new ParsedSourceFileResult(pipeline.SourceFile, parsedCityObjects, null, [], TimeSpan.Zero)],
                fallbackBounds: null);
            if (demBounds is null)
            {
                continue;
            }

            geometryScopedOverlays.AddRange(
                await LocalCityGmlDemBootstrapSupport.CreateDemTerrainTextureOverlaysAsync(
                    demBounds,
                    requestedMeshCodes,
                    demRasterCatalog,
                    cancellationToken));
        }

        return geometryScopedOverlays.Count > 0
            ? geometryScopedOverlays
                .Distinct()
                .OrderBy(static overlay => overlay.GeographicBounds.MinLatitude)
                .ThenBy(static overlay => overlay.GeographicBounds.MinLongitude)
                .ThenBy(static overlay => overlay.GeographicBounds.MaxLatitude)
                .ThenBy(static overlay => overlay.GeographicBounds.MaxLongitude)
                .ToArray()
            : requestScopedFallbackOverlays;
    }

    private static bool MatchesDemRequestedMeshCode(
        SourceFileDescriptor sourceFile,
        string requestedMeshCode)
    {
        if (string.Equals(sourceFile.MatchedMeshCode, requestedMeshCode, StringComparison.Ordinal))
        {
            return true;
        }

        return sourceFile.MatchedMeshCode.Length == 6
            && requestedMeshCode.Length >= 6
            && requestedMeshCode.StartsWith(sourceFile.MatchedMeshCode, StringComparison.Ordinal);
    }
}
