using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public static partial class LocalCityGmlObjectProjection
{
    internal static async Task<SourceFilePipeline[]> CreateSourceFilePipelinesAsync(
        IReadOnlyList<SourceFileDescriptor> sourceFiles,
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<MeshCodeBounds> requestedMeshAreas,
        Action<string>? progressReporter,
        LodFilteringStrategy lodFilteringStrategy,
        ICityGmlAppearanceStoreFactory appearanceStoreFactory,
        ICityGmlLodSelector lodSelector,
        CancellationToken cancellationToken)
    {
        global::Plateau.ResoniteLink.Application.Importing.SourceFilePipeline[] pipelines = await CreateSourceFilePipelinesCoreAsync(
            sourceFiles.Select(global::Plateau.ResoniteLink.Application.Importing.SourceFileDescriptor.FromLegacy).ToArray(),
            datasetSource,
            requestedMeshAreas,
            progressReporter,
            lodFilteringStrategy,
            appearanceStoreFactory,
            lodSelector,
            cancellationToken);

        return pipelines.Select(static pipeline => pipeline.ToLegacy()).ToArray();
    }

    internal static Task<ParsedSourceFileResult> ParseSourceFileAsync(
        SourceFileDescriptor sourceFile,
        IPlateauDatasetContentSource datasetSource,
        IReadOnlyList<MeshCodeBounds> requestedMeshAreas,
        Action<string>? progressReporter,
        LodFilteringStrategy lodFilteringStrategy,
        ICityGmlAppearanceStoreFactory appearanceStoreFactory,
        ICityGmlLodSelector lodSelector,
        CancellationToken cancellationToken)
    {
        return ParseSourceFileCoreAsync(
                global::Plateau.ResoniteLink.Application.Importing.SourceFileDescriptor.FromLegacy(sourceFile),
                datasetSource,
                requestedMeshAreas,
                progressReporter,
                lodFilteringStrategy,
                appearanceStoreFactory,
                lodSelector,
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
        IReadOnlyList<MeshCodeBounds> requestedMeshAreas,
        LodFilteringStrategy? lodFilteringStrategy,
        ICityGmlAppearanceStoreFactory appearanceStoreFactory,
        ICityGmlLodSelector lodSelector,
        CancellationToken cancellationToken = default)
    {
        return StreamParsedCityObjectsCoreAsync(
            sourceFile,
            datasetSource,
            requestedMeshAreas,
            lodFilteringStrategy,
            appearanceStoreFactory,
            lodSelector,
            parsedReferenceSystem: null,
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
