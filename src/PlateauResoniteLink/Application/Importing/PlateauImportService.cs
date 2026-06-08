using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using PlateauResoniteLink.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class PlateauImportService(
    ISceneSink sceneSink,
    ResolvePlateauDatasetSource resolveDatasetSource,
    CreateImportedSceneSource createImportedSceneSource,
    CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials,
    IArchiveFileLayoutPolicy archiveFileLayoutPolicy,
    ILoggerFactory? loggerFactory = null)
{
    private readonly ISceneSink sceneSink =
        sceneSink ?? throw new ArgumentNullException(nameof(sceneSink));
    private readonly ResolvePlateauDatasetSource resolveDatasetSource =
        resolveDatasetSource ?? throw new ArgumentNullException(nameof(resolveDatasetSource));
    private readonly ILoggerFactory loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    private readonly ILogger logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger("PlateauResoniteLink.Import");
    private readonly CreateImportedSceneSource createImportedSceneSource =
        createImportedSceneSource ?? throw new ArgumentNullException(nameof(createImportedSceneSource));
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
            ResolvedLocalPlateauImportRequest resolvedRequest = await resolveDatasetSource(
                validatedRequest,
                datasetWorkRoot,
                cancellationToken);
            logger.WriteDebug(
                "Resolved CityGML source for '{Dataset}' mesh-code '{MeshCode}'.",
                resolvedRequest.Dataset,
                resolvedRequest.MeshCode);

            Stopwatch sourceStopwatch = Stopwatch.StartNew();
            IImportedSceneSource importedSceneSource = await createImportedSceneSource(
                resolvedRequest,
                loggerFactory,
                cancellationToken);
            sourceStopwatch.Stop();
            logger.WriteDebug(
                "Prepared imported scene source in {ElapsedSeconds:F3}s.",
                sourceStopwatch.Elapsed.TotalSeconds);

            ImportedSceneMetadata metadata = importedSceneSource.Metadata;
            logger.WriteInformation(
                "Setup will use {CommonMaterialCount} codebase-reachable common materials.",
                commonMaterials.Count);

            SceneImportExecutionPlan executionPlan = SceneImportExecutionPlan.Create(
                resolvedRequest,
                metadata,
                datasetWorkRoot,
                this.commonMaterials);

            await importedSceneSource.ValidateBeforeSinkSetupAsync(cancellationToken);

            logger.WriteInformation("Starting live scene initialization with codebase-reachable common materials.");

            int sourceCityObjectCount = 0;
            Stopwatch cityObjectStopwatch = Stopwatch.StartNew();
            logger.WriteInformation("Handing object unit stream to sink.");
            CountingImportedObjectUnitStream countedObjectUnits = new(
                importedSceneSource.ReadObjectUnitsAsync(cancellationToken),
                cityObjectCount => sourceCityObjectCount += cityObjectCount);
            SceneImportExecutionResult executionResult = await sceneSink.ExecuteAsync(
                executionPlan,
                countedObjectUnits.ReadAllAsync(cancellationToken),
                cancellationToken);

            cityObjectStopwatch.Stop();
            logger.WriteInformation(
                "Streamed {CityObjectCount} city objects in {ElapsedSeconds:F3}s.",
                sourceCityObjectCount,
                cityObjectStopwatch.Elapsed.TotalSeconds);
            logger.WriteDebug(
                "City object streaming elapsed {ElapsedSeconds:F3}s after sink execution started.",
                cityObjectStopwatch.Elapsed.TotalSeconds);

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

}
