using System.Diagnostics;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Application.Importing;

public sealed class PlateauImportService(
    IResoniteSceneBuilder sceneBuilder,
    IPlateauDatasetSourceResolver? datasetSourceResolver = null,
    Action<string>? progressReporter = null)
{
    private readonly IResoniteSceneBuilder sceneBuilder = sceneBuilder;
    private readonly IPlateauDatasetSourceResolver datasetSourceResolver =
        datasetSourceResolver ?? new CkanPlateauDatasetSourceResolver();
    private readonly Action<string>? progressReporter = progressReporter;

    public async Task<ImportExecutionResult> ExecuteAsync(
        PlateauImportRequest request,
        string workRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);

        IReadOnlyList<string> validationErrors = PlateauImportRequestValidator.Validate(request);
        if (validationErrors.Count > 0)
        {
            throw new PlateauImportValidationException(validationErrors);
        }

        PlateauImportRequest normalizedRequest = request with
        {
            Dataset = request.Dataset.Trim(),
            MeshCode = request.MeshCode.Trim(),
            LocalSourcePath = string.IsNullOrWhiteSpace(request.LocalSourcePath) ? null : request.LocalSourcePath.Trim(),
            PackageNames = request.PackageNames is null
                ? null
                : PlateauPackageCatalog.NormalizeRequestedPackageNames(request.PackageNames),
        };

        PlateauImportRequest resolvedRequest =
            await datasetSourceResolver.ResolveAsync(normalizedRequest, workRoot, cancellationToken);
        ReportProgress(
            $"[import] Resolved dataset source for '{resolvedRequest.Dataset}' mesh '{resolvedRequest.MeshCode}'.");

        Stopwatch sourceStopwatch = Stopwatch.StartNew();
        IResoniteConstructionSource source = await LocalCityGmlResonitePlanBuilder.CreateConstructionSourceAsync(
            resolvedRequest,
            progressReporter,
            cancellationToken);
        sourceStopwatch.Stop();
        ReportProgress(
            $"[import] Prepared construction source in {sourceStopwatch.Elapsed.TotalSeconds:F3}s.");

        try
        {
            Stopwatch beginStopwatch = Stopwatch.StartNew();
            await sceneBuilder.BeginAsync(source.Metadata, workRoot, cancellationToken);
            beginStopwatch.Stop();
            ReportProgress($"[import] Scene builder initialization completed in {beginStopwatch.Elapsed.TotalSeconds:F3}s.");

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
                $"[import] Streamed {processedCityObjectCount} city objects in {cityObjectStopwatch.Elapsed.TotalSeconds:F3}s.");

            if (!processedAnyCityObject)
            {
                throw new PlateauImportValidationException(
                    [$"No triangulated CityGML geometry was produced for mesh code '{resolvedRequest.MeshCode}'."]);
            }

            Stopwatch completeStopwatch = Stopwatch.StartNew();
            IReadOnlyList<string> destinations = await sceneBuilder.CompleteAsync(cancellationToken);
            completeStopwatch.Stop();
            ReportProgress(
                $"[import] Scene builder completion finished in {completeStopwatch.Elapsed.TotalSeconds:F3}s.");
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
