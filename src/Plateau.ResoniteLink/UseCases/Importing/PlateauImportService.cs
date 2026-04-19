using System.Diagnostics;
using System.Runtime.CompilerServices;

using Plateau.ResoniteLink.Application.Logging;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public sealed class PlateauImportService(
    ISceneImportTarget sceneBuilder,
    IPlateauDatasetSourceResolver datasetSourceResolver,
    ICityGmlDocumentReader documentReader,
    IResoniteConstructionSourceFactory constructionSourceFactory,
    IArchiveFileLayoutPolicy archiveFileLayoutPolicy,
    Action<string>? progressReporter = null)
{
    public PlateauImportService(
        ISceneImportTarget sceneBuilder,
        IPlateauDatasetSourceResolver datasetSourceResolver,
        ICityGmlDocumentReader documentReader,
        IResoniteConstructionSourceFactory constructionSourceFactory,
        Action<string>? progressReporter = null)
        : this(
            sceneBuilder,
            datasetSourceResolver,
            documentReader,
            constructionSourceFactory,
            new ArchiveFileLayoutPolicy(),
            progressReporter)
    {
    }

    private readonly ISceneImportTarget sceneBuilder =
        sceneBuilder ?? throw new ArgumentNullException(nameof(sceneBuilder));
    private readonly IPlateauDatasetSourceResolver datasetSourceResolver =
        datasetSourceResolver ?? throw new ArgumentNullException(nameof(datasetSourceResolver));
    private readonly ICityGmlDocumentReader documentReader =
        documentReader ?? throw new ArgumentNullException(nameof(documentReader));
    private readonly Action<string>? progressReporter = progressReporter;
    private readonly IResoniteConstructionSourceFactory constructionSourceFactory =
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
            LocalCityGmlDocumentSet documentSet = await documentReader.ReadAsync(
                resolvedRequest,
                progressReporter,
                cancellationToken);
            setupStopwatch.Stop();
            ReportProgress(
                PlateauLog.Info("import", $"Setup discovery completed in {setupStopwatch.Elapsed.TotalSeconds:F3}s."));

            Stopwatch sourceStopwatch = Stopwatch.StartNew();
            IResoniteConstructionSource source = await constructionSourceFactory.CreateAsync(
                resolvedRequest,
                documentSet,
                progressReporter,
                cancellationToken);
            sourceStopwatch.Stop();
            ReportProgress(
                PlateauLog.Debug("import", $"Prepared construction source in {sourceStopwatch.Elapsed.TotalSeconds:F3}s."));
            ConstructionMetadata metadata = SceneImportContractMapper.ToContract(source.Metadata);
            SceneImportExecutionPlan executionPlan = SceneImportExecutionPlan.Create(
                normalizedRequest,
                metadata,
                documentSet.DatasetSource,
                datasetWorkRoot);
            ReportProgress(
                PlateauLog.Info(
                    "import",
                    $"Starting live scene initialization ({metadata.SourceDataset.PackageNames.Count} package-scoped common material families)."));

            ReportProgress(PlateauLog.Info("import", "Starting city object streaming."));

            await using IAsyncEnumerator<ResoniteConstructionCityObject> cityObjectEnumerator =
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

            return new ImportExecutionResult(metadata, executionResult.Destinations);
        }
        finally
        {
            await sceneBuilder.DisposeAsync();
        }
    }

    private void ReportProgress(string message)
    {
        progressReporter?.Invoke(message);
    }

    private static async IAsyncEnumerable<ImportedCityObject> ReadImportedCityObjectsAsync(
        ResoniteConstructionCityObject firstCityObject,
        IAsyncEnumerator<ResoniteConstructionCityObject> remainingCityObjects,
        Action onReadAdditionalCityObject,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return SceneImportContractMapper.ToContract(firstCityObject);

        while (await remainingCityObjects.MoveNextAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            onReadAdditionalCityObject();
            yield return SceneImportContractMapper.ToContract(remainingCityObjects.Current);
        }
    }
}
