using System.Diagnostics;

using Plateau.ResoniteLink.Application.Logging;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public sealed class PlateauImportService(
    IResoniteSceneBuilder sceneBuilder,
    IPlateauDatasetSourceResolver datasetSourceResolver,
    IResoniteConstructionSourceFactory constructionSourceFactory,
    Action<string>? progressReporter = null)
{
    private readonly IResoniteSceneBuilder sceneBuilder =
        sceneBuilder ?? throw new ArgumentNullException(nameof(sceneBuilder));
    private readonly IPlateauDatasetSourceResolver datasetSourceResolver =
        datasetSourceResolver ?? throw new ArgumentNullException(nameof(datasetSourceResolver));
    private readonly Action<string>? progressReporter = progressReporter;
    private readonly IResoniteConstructionSourceFactory constructionSourceFactory =
        constructionSourceFactory ?? throw new ArgumentNullException(nameof(constructionSourceFactory));

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
            Stopwatch connectStopwatch = Stopwatch.StartNew();
            await sceneBuilder.EnsureConnectedAsync(normalizedRequest, cancellationToken);
            connectStopwatch.Stop();
            ReportProgress(
                PlateauLog.Debug("import", $"Scene builder connection check completed in {connectStopwatch.Elapsed.TotalSeconds:F3}s."));

            Stopwatch sourceStopwatch = Stopwatch.StartNew();
            IResoniteConstructionSource source = await constructionSourceFactory.CreateAsync(
                resolvedRequest,
                progressReporter,
                cancellationToken);
            sourceStopwatch.Stop();
            ReportProgress(
                PlateauLog.Debug("import", $"Prepared construction source in {sourceStopwatch.Elapsed.TotalSeconds:F3}s."));

            Stopwatch beginStopwatch = Stopwatch.StartNew();
            ReportProgress(PlateauLog.Info("import", "Starting live scene initialization."));
            await sceneBuilder.BeginAsync(source.Metadata, datasetWorkRoot, cancellationToken);
            beginStopwatch.Stop();
            ReportProgress(PlateauLog.Debug("import", $"Scene builder initialization completed in {beginStopwatch.Elapsed.TotalSeconds:F3}s."));

            bool processedAnyCityObject = false;
            int processedCityObjectCount = 0;
            Stopwatch cityObjectStopwatch = Stopwatch.StartNew();

            await foreach (ResoniteConstructionCityObject cityObject in source.ReadCityObjectsAsync(cancellationToken))
            {
                processedAnyCityObject = true;
                await sceneBuilder.ProcessCityObjectAsync(cityObject, cancellationToken);
                processedCityObjectCount++;
            }

            cityObjectStopwatch.Stop();
            ReportProgress(
                PlateauLog.Info("import", $"Streamed {processedCityObjectCount} city objects in {cityObjectStopwatch.Elapsed.TotalSeconds:F3}s."));

            if (!processedAnyCityObject)
            {
                throw new PlateauImportValidationException(
                    [$"No triangulated CityGML geometry was produced for mesh code '{resolvedRequest.MeshCode}'."]);
            }

            Stopwatch completeStopwatch = Stopwatch.StartNew();
            IReadOnlyList<string> destinations = await sceneBuilder.CompleteAsync(cancellationToken);
            completeStopwatch.Stop();
            ReportProgress(
                PlateauLog.Debug("import", $"Scene builder completion finished in {completeStopwatch.Elapsed.TotalSeconds:F3}s."));
            return new ImportExecutionResult(source.Metadata, destinations);
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
}
