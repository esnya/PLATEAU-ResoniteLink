using System.Diagnostics;

using Plateau.ResoniteLink.Application.Logging;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public sealed class PlateauImportService(
    ISceneImportTarget sceneBuilder,
    IPlateauDatasetSourceResolver datasetSourceResolver,
    ICityGmlDocumentReader documentReader,
    IResoniteConstructionSourceFactory constructionSourceFactory,
    Action<string>? progressReporter = null)
{
    private readonly ISceneImportTarget sceneBuilder =
        sceneBuilder ?? throw new ArgumentNullException(nameof(sceneBuilder));
    private readonly IPlateauDatasetSourceResolver datasetSourceResolver =
        datasetSourceResolver ?? throw new ArgumentNullException(nameof(datasetSourceResolver));
    private readonly ICityGmlDocumentReader documentReader =
        documentReader ?? throw new ArgumentNullException(nameof(documentReader));
    private readonly Action<string>? progressReporter = progressReporter;
    private readonly IResoniteConstructionSourceFactory constructionSourceFactory =
        constructionSourceFactory ?? throw new ArgumentNullException(nameof(constructionSourceFactory));

    public PlateauImportService(
        ISceneImportTarget sceneBuilder,
        IPlateauDatasetSourceResolver datasetSourceResolver,
        IResoniteConstructionSourceFactory constructionSourceFactory,
        Action<string>? progressReporter = null)
        : this(
            sceneBuilder,
            datasetSourceResolver,
            new LocalCityGmlDocumentReader(),
            constructionSourceFactory,
            progressReporter)
    {
    }

    public async Task<ImportExecutionResult> ExecuteAsync(
        PlateauImportRequest request,
        string workRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);

        ValidatedPlateauImportRequest validatedRequest = PlateauImportRequestValidator.NormalizeAndValidateOrThrow(request);
        PlateauImportRequest normalizedRequest = validatedRequest.ToImportRequest();
        string datasetWorkRoot = WorkRootLayout.ResolveDatasetRoot(workRoot, validatedRequest.Dataset);

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

            Stopwatch connectStopwatch = Stopwatch.StartNew();
            await sceneBuilder.EnsureConnectedAsync(normalizedRequest, cancellationToken);
            connectStopwatch.Stop();
            ReportProgress(
                PlateauLog.Debug("import", $"Scene builder connection check completed in {connectStopwatch.Elapsed.TotalSeconds:F3}s."));

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
            SceneBuildRequest sceneBuildRequest = CreateSceneBuildRequest(metadata, documentSet.DatasetSource, datasetWorkRoot);
            ReportProgress(
                PlateauLog.Info(
                    "import",
                    $"Starting live scene initialization ({metadata.SourceDataset.PackageNames.Count} package-scoped common material families)."));

            Stopwatch beginStopwatch = Stopwatch.StartNew();
            await sceneBuilder.BeginAsync(sceneBuildRequest, cancellationToken);
            beginStopwatch.Stop();
            ReportProgress(
                PlateauLog.Debug("import", $"Scene builder initialization completed in {beginStopwatch.Elapsed.TotalSeconds:F3}s."));
            ReportProgress(PlateauLog.Info("import", "Starting city object streaming."));

            bool processedAnyCityObject = false;
            int processedCityObjectCount = 0;
            Stopwatch cityObjectStopwatch = Stopwatch.StartNew();
            await foreach (ResoniteConstructionCityObject cityObject in source.ReadCityObjectsAsync(cancellationToken))
            {
                processedAnyCityObject = true;
                await sceneBuilder.ProcessCityObjectAsync(SceneImportContractMapper.ToContract(cityObject), cancellationToken);
                processedCityObjectCount++;
            }

            cityObjectStopwatch.Stop();
            ReportProgress(
                PlateauLog.Info("import", $"Streamed {processedCityObjectCount} city objects in {cityObjectStopwatch.Elapsed.TotalSeconds:F3}s."));
            ReportProgress(
                PlateauLog.Debug(
                    "import",
                    $"City object streaming elapsed {cityObjectStopwatch.Elapsed.TotalSeconds:F3}s after prewarm started."));

            if (!processedAnyCityObject)
            {
                throw new PlateauImportValidationException(
                    [$"No triangulated CityGML geometry was produced for mesh code '{resolvedRequest.MeshCode}'."]);
            }

            Stopwatch completeStopwatch = Stopwatch.StartNew();
            ReportProgress(PlateauLog.Info("import", "Starting live scene completion."));
            IReadOnlyList<string> destinations = await sceneBuilder.CompleteAsync(cancellationToken);
            completeStopwatch.Stop();
            ReportProgress(
                PlateauLog.Debug("import", $"Scene builder completion finished in {completeStopwatch.Elapsed.TotalSeconds:F3}s."));
            return new ImportExecutionResult(metadata, destinations);
        }
        finally
        {
            await sceneBuilder.DisposeAsync();
        }
    }

    private static SceneBuildRequest CreateSceneBuildRequest(
        ConstructionMetadata metadata,
        IPlateauDatasetContentSource datasetContentSource,
        string workRoot)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(datasetContentSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);

        return new SceneBuildRequest(
            metadata,
            datasetContentSource,
            workRoot);
    }

    private void ReportProgress(string message)
    {
        progressReporter?.Invoke(message);
    }
}
