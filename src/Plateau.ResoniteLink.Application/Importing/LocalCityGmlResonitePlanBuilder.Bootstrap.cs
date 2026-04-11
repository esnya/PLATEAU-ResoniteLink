using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public static partial class LocalCityGmlResonitePlanBuilder
{
    public static Task<IResoniteConstructionSource> CreateConstructionSourceAsync(
        PlateauImportRequest request,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        return new LocalCityGmlConstructionSourceFactory().CreateAsync(
            request,
            progressReporter,
            cancellationToken);
    }

    public static IResoniteConstructionSource CreateConstructionSource(
        PlateauImportRequest request,
        Action<string>? progressReporter = null)
    {
        return CreateConstructionSourceAsync(request, progressReporter).GetAwaiter().GetResult();
    }

    internal static async Task<SourceFilePipeline[]> CreateSourceFilePipelinesAsync(
        IReadOnlyList<SourceFileDescriptor> sourceFiles,
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<MeshCodeArea> requestedMeshAreas,
        Action<string>? progressReporter,
        LodFilteringStrategy lodFilteringStrategy,
        CancellationToken cancellationToken)
    {
        global::Plateau.ResoniteLink.Application.Importing.SourceFilePipeline[] pipelines = await CreateSourceFilePipelinesCoreAsync(
            sourceFiles.Select(global::Plateau.ResoniteLink.Application.Importing.SourceFileDescriptor.FromLegacy).ToArray(),
            datasetSource,
            requestedMeshAreas,
            progressReporter,
            lodFilteringStrategy,
            cancellationToken);

        return pipelines.Select(static pipeline => pipeline.ToLegacy()).ToArray();
    }

    internal static Task<ParsedSourceFileResult> ParseSourceFileAsync(
        SourceFileDescriptor sourceFile,
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<MeshCodeArea> requestedMeshAreas,
        Action<string>? progressReporter,
        LodFilteringStrategy lodFilteringStrategy,
        CancellationToken cancellationToken)
    {
        return ParseSourceFileCoreAsync(
                global::Plateau.ResoniteLink.Application.Importing.SourceFileDescriptor.FromLegacy(sourceFile),
                datasetSource,
                requestedMeshAreas,
                progressReporter,
                lodFilteringStrategy,
                cancellationToken)
            .ContinueWith(
                static task => task.GetAwaiter().GetResult().ToLegacy(),
                cancellationToken,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }

    internal static IAsyncEnumerable<ParsedCityObject> StreamParsedCityObjectsAsync(
        SourceFileDescriptor sourceFile,
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<MeshCodeArea> requestedMeshAreas,
        LodFilteringStrategy? lodFilteringStrategy,
        CancellationToken cancellationToken = default)
    {
        return StreamParsedCityObjectsCoreAsync(
            sourceFile,
            datasetSource,
            requestedMeshAreas,
            lodFilteringStrategy,
            cancellationToken);
    }

    internal static Task<CoordinateReferenceSystem> ReadDocumentReferenceSystemAsync(
        IPlateauDatasetContentSource datasetSource,
        string relativePath,
        CancellationToken cancellationToken)
    {
        return ReadDocumentReferenceSystemCoreAsync(
            datasetSource,
            relativePath,
            cancellationToken);
    }
}
