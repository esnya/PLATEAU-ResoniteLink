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

        ResolvedDemTextureSources resolvedDemTextureSources = await demTextureSourcePolicy.ResolveAsync(
            request,
            documentSet.RequestedMeshCodes.Count > 0
                ? documentSet.RequestedMeshCodes
                : [request.MeshCode],
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
            documentSet.RequestedMeshCodes);
        LocalCityGmlBootstrapContext resolvedBootstrapContext = new(
            readResult.BootstrapContext.SourceFilePipelines,
            readResult.BootstrapContext.GlobalOriginPoint);
        return new LocalCityGmlDocumentReadResult(
            resolvedDocumentSet,
            resolvedBootstrapContext);
    }
}
