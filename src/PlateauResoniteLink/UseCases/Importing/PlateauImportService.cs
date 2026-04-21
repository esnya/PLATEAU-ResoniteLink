using System.Diagnostics;
using System.Runtime.CompilerServices;

using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

public sealed class PlateauImportService(
    ISceneImportTarget sceneBuilder,
    IPlateauDatasetSourceResolver datasetSourceResolver,
    ICityGmlDocumentReader documentReader,
    IImportedSceneSourceFactory constructionSourceFactory,
    IArchiveFileLayoutPolicy archiveFileLayoutPolicy,
    Action<string>? progressReporter = null)
{
    private readonly ISceneImportTarget sceneBuilder =
        sceneBuilder ?? throw new ArgumentNullException(nameof(sceneBuilder));
    private readonly IPlateauDatasetSourceResolver datasetSourceResolver =
        datasetSourceResolver ?? throw new ArgumentNullException(nameof(datasetSourceResolver));
    private readonly ICityGmlDocumentReader documentReader =
        documentReader ?? throw new ArgumentNullException(nameof(documentReader));
    private readonly Action<string>? progressReporter = progressReporter;
    private readonly IImportedSceneSourceFactory constructionSourceFactory =
        constructionSourceFactory ?? throw new ArgumentNullException(nameof(constructionSourceFactory));
    private readonly IArchiveFileLayoutPolicy archiveFileLayoutPolicy =
        archiveFileLayoutPolicy ?? throw new ArgumentNullException(nameof(archiveFileLayoutPolicy));

    public async Task<ImportExecutionResult> ExecuteAsync(
        PlateauImportRequest request,
        string workRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);

        ValidatedPlateauImportRequest validatedRequest = PlateauImportRequestValidator.NormalizeAndValidateOrThrow(request);
        PlateauImportRequest normalizedRequest = validatedRequest.ToImportRequest();
        string datasetWorkRoot = archiveFileLayoutPolicy.ResolveDatasetRoot(workRoot, validatedRequest.Dataset);

        PlateauImportRequest resolvedRequest =
            (await datasetSourceResolver.ResolveAsync(validatedRequest, datasetWorkRoot, cancellationToken)).ToImportRequest();
        ReportProgress(
            PlateauLog.Debug("import", $"Resolved dataset source for '{resolvedRequest.Dataset}' mesh '{resolvedRequest.MeshCode}'."));

        try
        {
            Stopwatch setupStopwatch = Stopwatch.StartNew();
            LocalCityGmlDocumentReadResult readResult = await documentReader.ReadAsync(
                resolvedRequest,
                progressReporter,
                cancellationToken);
            LocalCityGmlDocumentSet documentSet = readResult.DocumentSet;
            setupStopwatch.Stop();
            ReportProgress(
                PlateauLog.Info("import", $"Setup discovery completed in {setupStopwatch.Elapsed.TotalSeconds:F3}s."));

            Stopwatch sourceStopwatch = Stopwatch.StartNew();
            IImportedSceneSource source = await constructionSourceFactory.CreateAsync(
                resolvedRequest,
                readResult,
                progressReporter,
                cancellationToken);
            sourceStopwatch.Stop();
            ReportProgress(
                PlateauLog.Debug("import", $"Prepared construction source in {sourceStopwatch.Elapsed.TotalSeconds:F3}s."));

            Stopwatch commonMaterialStopwatch = Stopwatch.StartNew();
            IReadOnlyList<ResoniteMaterialBinding> discoveredCommonMaterials = await ReadCommonMaterialsAsync(
                source,
                cancellationToken);
            commonMaterialStopwatch.Stop();
            ReportProgress(
                PlateauLog.Info(
                    "import",
                    $"Read {discoveredCommonMaterials.Count} shared common materials from source in {commonMaterialStopwatch.Elapsed.TotalSeconds:F3}s."));
            IReadOnlyList<ResoniteMaterialBinding>? commonMaterials = discoveredCommonMaterials.Count == 0
                ? null
                : discoveredCommonMaterials;

            ImportedSceneMetadata metadata = source.Metadata;
            SceneImportExecutionPlan executionPlan = SceneImportExecutionPlan.Create(
                normalizedRequest,
                resolvedRequest,
                metadata,
                documentSet.DatasetSource.SourcePath,
                datasetWorkRoot,
                commonMaterials);
            ReportProgress(
                PlateauLog.Info(
                    "import",
                    $"Starting live scene initialization ({metadata.SourceDataset.PackageNames.Count} package-scoped common material families)."));

            ReportProgress(PlateauLog.Info("import", "Starting city object streaming."));

            await using IAsyncEnumerator<ImportedCityObject> cityObjectEnumerator =
                source.ReadCityObjectsAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
            if (!await cityObjectEnumerator.MoveNextAsync())
            {
                throw new PlateauImportValidationException(
                    [$"No triangulated CityGML geometry was produced for mesh code '{resolvedRequest.MeshCode}'."]);
            }

            int sourceCityObjectCount = 1;
            Stopwatch cityObjectStopwatch = Stopwatch.StartNew();
            SceneImportExecutionResult executionResult = await sceneBuilder.ExecuteAsync(
                executionPlan,
                ReadImportedCityObjectsAsync(
                    cityObjectEnumerator.Current,
                    cityObjectEnumerator,
                    () => sourceCityObjectCount++,
                    cancellationToken),
                cancellationToken);

            cityObjectStopwatch.Stop();
            ReportProgress(
                PlateauLog.Info("import", $"Streamed {sourceCityObjectCount} city objects in {cityObjectStopwatch.Elapsed.TotalSeconds:F3}s."));
            ReportProgress(
                PlateauLog.Debug(
                    "import",
                    $"City object streaming elapsed {cityObjectStopwatch.Elapsed.TotalSeconds:F3}s after prewarm started."));

            if (executionResult.ProcessedCityObjectCount == 0
                && executionResult.FailedCityObjectCount > 0)
            {
                throw new InvalidOperationException(
                    $"Live send failed for all {sourceCityObjectCount} city objects "
                    + $"(failed={executionResult.FailedCityObjectCount}).");
            }

            return new ImportExecutionResult(
                metadata,
                executionResult.Destinations,
                CreateDataSourceUsages(metadata, executionResult));
        }
        finally
        {
            await sceneBuilder.DisposeAsync();
        }
    }

    private static async Task<IReadOnlyList<ResoniteMaterialBinding>> ReadCommonMaterialsAsync(
        IImportedSceneSource source,
        CancellationToken cancellationToken)
    {
        Dictionary<string, ResoniteMaterialBinding> materialByKey = new(StringComparer.Ordinal);
        await foreach (MaterialBinding material in source.ReadCommonMaterialsAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResoniteMaterialBinding internalMaterial = SceneImportContractMapper.ToInternal(material);
            materialByKey.TryAdd(internalMaterial.MaterialKey, internalMaterial);
        }

        return materialByKey.Values.ToArray();
    }

    private static List<ImportDataSourceUsage> CreateDataSourceUsages(
        ImportedSceneMetadata metadata,
        SceneImportExecutionResult executionResult)
    {
        List<ImportDataSourceUsage> usages = metadata.SourceDataset.SourceFiles
            .Select(static sourceFile => new ImportDataSourceUsage(
                ImportDataSourceCategory.CityGmlSourceFile,
                sourceFile,
                UsedCount: 1))
            .ToList();

        if (executionResult.DataSourceUsages is { Count: > 0 })
        {
            usages.AddRange(executionResult.DataSourceUsages);
        }

        return usages;
    }

    private void ReportProgress(string message)
    {
        progressReporter?.Invoke(message);
    }

    private static async IAsyncEnumerable<ImportedCityObject> ReadImportedCityObjectsAsync(
        ImportedCityObject firstCityObject,
        IAsyncEnumerator<ImportedCityObject> remainingCityObjects,
        Action onReadAdditionalCityObject,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return firstCityObject;

        while (await remainingCityObjects.MoveNextAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            onReadAdditionalCityObject();
            yield return remainingCityObjects.Current;
        }
    }
}
