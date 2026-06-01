using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class PlateauImportService(
    ISceneSink sceneSink,
    IPlateauDatasetSourceResolver datasetSourceResolver,
    IImportedSceneSourceFactory importedSceneSourceFactory,
    CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials,
    IArchiveFileLayoutPolicy archiveFileLayoutPolicy,
    Action<string>? progressReporter = null)
{
    private readonly ISceneSink sceneSink =
        sceneSink ?? throw new ArgumentNullException(nameof(sceneSink));
    private readonly IPlateauDatasetSourceResolver datasetSourceResolver =
        datasetSourceResolver ?? throw new ArgumentNullException(nameof(datasetSourceResolver));
    private readonly Action<string>? progressReporter = progressReporter;
    private readonly IImportedSceneSourceFactory importedSceneSourceFactory =
        importedSceneSourceFactory ?? throw new ArgumentNullException(nameof(importedSceneSourceFactory));
    private readonly CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials =
        commonMaterials ?? throw new ArgumentNullException(nameof(commonMaterials));
    private readonly IArchiveFileLayoutPolicy archiveFileLayoutPolicy =
        archiveFileLayoutPolicy ?? throw new ArgumentNullException(nameof(archiveFileLayoutPolicy));

    public async Task<ImportExecutionResult> ExecuteAsync(
        PlateauImportRequest request,
        string workRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);

        ValidatedPlateauImportRequest validatedRequest = PlateauImportRequestValidator.NormalizeAndValidateOrThrow(request);
        string datasetWorkRoot = archiveFileLayoutPolicy.ResolveDatasetRoot(workRoot, validatedRequest.Dataset);
        ExceptionDispatchInfo? failure = null;

        try
        {
            ResolvedLocalPlateauImportRequest resolvedRequest = await datasetSourceResolver.ResolveAsync(
                validatedRequest,
                datasetWorkRoot,
                cancellationToken);
            ReportProgress(
                PlateauLog.Debug("import", $"Resolved CityGML source for '{resolvedRequest.Dataset}' mesh-code '{resolvedRequest.MeshCode}'."));

            Stopwatch sourceStopwatch = Stopwatch.StartNew();
            IImportedSceneSource importedSceneSource = await importedSceneSourceFactory.CreateAsync(
                resolvedRequest,
                progressReporter,
                cancellationToken);
            sourceStopwatch.Stop();
            ReportProgress(
                PlateauLog.Debug("import", $"Prepared imported scene source in {sourceStopwatch.Elapsed.TotalSeconds:F3}s."));

            ImportedSceneMetadata metadata = importedSceneSource.Metadata;
            ReportProgress(
                PlateauLog.Info(
                    "import",
                    $"Setup will use {this.commonMaterials.Count} codebase-reachable common materials."));

            SceneImportExecutionPlan executionPlan = SceneImportExecutionPlan.Create(
                resolvedRequest,
                metadata,
                datasetWorkRoot,
                this.commonMaterials);

            if (importedSceneSource is IImportedSceneSourcePreflight preflight)
            {
                await preflight.ValidateBeforeSinkSetupAsync(cancellationToken);
            }

            ReportProgress(
                PlateauLog.Info(
                    "import",
                    "Starting live scene initialization with codebase-reachable common materials."));

            int sourceCityObjectCount = 0;
            Stopwatch cityObjectStopwatch = Stopwatch.StartNew();
            ReportProgress(PlateauLog.Info("import", "Handing object unit stream to sink."));
            CountingImportedObjectUnitStream countedObjectUnits = new(
                importedSceneSource.ReadObjectUnitsAsync(cancellationToken),
                cityObjectCount => sourceCityObjectCount += cityObjectCount);
            SceneImportExecutionResult executionResult = await sceneSink.ExecuteAsync(
                executionPlan,
                countedObjectUnits.ReadAllAsync(cancellationToken),
                cancellationToken);

            cityObjectStopwatch.Stop();
            ReportProgress(
                PlateauLog.Info("import", $"Streamed {sourceCityObjectCount} city objects in {cityObjectStopwatch.Elapsed.TotalSeconds:F3}s."));
            ReportProgress(
                PlateauLog.Debug(
                    "import",
                    $"City object streaming elapsed {cityObjectStopwatch.Elapsed.TotalSeconds:F3}s after sink execution started."));

            if (sourceCityObjectCount == 0)
            {
                throw new PlateauImportValidationException(
                    [$"No triangulated CityGML geometry was produced for mesh-code '{resolvedRequest.MeshCode}'."]);
            }

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

        if (executionResult.DataSourceUsages.Count > 0)
        {
            usages.AddRange(executionResult.DataSourceUsages);
        }

        return usages;
    }

    private void ReportProgress(string message)
    {
        progressReporter?.Invoke(message);
    }

}
