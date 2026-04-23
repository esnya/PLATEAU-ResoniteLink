using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class PlateauImportService(
    ISceneSink sceneSink,
    IPlateauDatasetSourceResolver datasetSourceResolver,
    IImportedSceneSourceFactory constructionSourceFactory,
    CommonMaterialCatalog commonMaterialCatalog,
    IArchiveFileLayoutPolicy archiveFileLayoutPolicy,
    Action<string>? progressReporter = null)
{
    private readonly ISceneSink sceneSink =
        sceneSink ?? throw new ArgumentNullException(nameof(sceneSink));
    private readonly IPlateauDatasetSourceResolver datasetSourceResolver =
        datasetSourceResolver ?? throw new ArgumentNullException(nameof(datasetSourceResolver));
    private readonly Action<string>? progressReporter = progressReporter;
    private readonly IImportedSceneSourceFactory constructionSourceFactory =
        constructionSourceFactory ?? throw new ArgumentNullException(nameof(constructionSourceFactory));
    private readonly CommonMaterialCatalog commonMaterialCatalog =
        commonMaterialCatalog ?? throw new ArgumentNullException(nameof(commonMaterialCatalog));
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
        ExceptionDispatchInfo? failure = null;

        try
        {
            PlateauImportRequest resolvedRequest =
                (await datasetSourceResolver.ResolveAsync(
                    validatedRequest,
                    datasetWorkRoot,
                    cancellationToken)).ToImportRequest();
            ReportProgress(
                PlateauLog.Debug("import", $"Resolved dataset source for '{resolvedRequest.Dataset}' mesh '{resolvedRequest.MeshCode}'."));

            Stopwatch sourceStopwatch = Stopwatch.StartNew();
            IImportedSceneSource source = await constructionSourceFactory.CreateAsync(
                resolvedRequest,
                progressReporter,
                cancellationToken);
            sourceStopwatch.Stop();
            ReportProgress(
                PlateauLog.Debug("import", $"Prepared construction source in {sourceStopwatch.Elapsed.TotalSeconds:F3}s."));

            ImportedSceneMetadata metadata = source.Metadata;
            IReadOnlyList<MaterialBinding> commonMaterials = commonMaterialCatalog.CreateForPackages(metadata.SourceDataset.PackageNames);
            ReportProgress(
                PlateauLog.Info(
                    "import",
                    $"Setup will use {commonMaterials.Count} package-catalog common materials."));

            SceneImportExecutionPlan executionPlan = SceneImportExecutionPlan.Create(
                normalizedRequest,
                resolvedRequest,
                metadata,
                resolvedRequest.LocalSourcePath!,
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
            SceneImportExecutionResult executionResult = await sceneSink.ExecuteAsync(
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
        catch (Exception exception)
        {
            failure = ExceptionDispatchInfo.Capture(exception);
            throw;
        }
        finally
        {
            try
            {
                await sceneSink.DisposeAsync();
            }
#pragma warning disable CA1031
            catch when (failure is not null)
            {
            }
#pragma warning restore CA1031
        }
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
