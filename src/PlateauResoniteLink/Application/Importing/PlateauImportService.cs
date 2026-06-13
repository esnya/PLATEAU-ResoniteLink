using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Diagnostics;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal sealed class PlateauImportService(
    ISceneSink sceneSink,
    IPlateauDatasetSourceResolver datasetSourceResolver,
    IImportedSceneSourceFactory importedSceneSourceFactory,
    CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials,
    IArchiveFileLayoutPolicy archiveFileLayoutPolicy)
{
    private readonly ISceneSink sceneSink =
        sceneSink ?? throw new ArgumentNullException(nameof(sceneSink));
    private readonly IPlateauDatasetSourceResolver datasetSourceResolver =
        datasetSourceResolver ?? throw new ArgumentNullException(nameof(datasetSourceResolver));
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
            PlateauDiagnostics.Verbose(
                "Resolved CityGML source for '{Dataset}' mesh-code '{MeshCode}'.",
                resolvedRequest.Dataset,
                resolvedRequest.MeshCode);

            Stopwatch sourceStopwatch = Stopwatch.StartNew();
            IImportedSceneSource importedSceneSource;
            using (Activity? sourceActivity = PlateauDiagnostics.StartActivity("plateau.import.source"))
            {
                importedSceneSource = await importedSceneSourceFactory.CreateAsync(
                    resolvedRequest,
                    cancellationToken);
            }
            sourceStopwatch.Stop();
            PlateauDiagnostics.Verbose(
                "Prepared imported scene source in {ElapsedSeconds:F3}s.",
                sourceStopwatch.Elapsed.TotalSeconds);

            ImportedSceneMetadata metadata = importedSceneSource.Metadata;
            PlateauDiagnostics.Progress(
                "Import source prepared for live send (dataset='{Dataset}', mesh='{MeshCode}', common_materials={CommonMaterialCount}).",
                resolvedRequest.Dataset,
                resolvedRequest.MeshCode,
                commonMaterials.Count);

            SceneImportExecutionPlan executionPlan = SceneImportExecutionPlan.Create(
                resolvedRequest,
                metadata,
                datasetWorkRoot,
                this.commonMaterials);

            if (importedSceneSource is IImportedSceneSourcePreflight preflight)
            {
                await preflight.ValidateBeforeSinkSetupAsync(cancellationToken);
            }

            PlateauDiagnostics.Progress("Starting live scene initialization.");

            int sourceCityObjectCount = 0;
            Stopwatch cityObjectStopwatch = Stopwatch.StartNew();
            PlateauDiagnostics.Verbose("Handing object unit stream to sink.");
            CountingImportedObjectUnitStream countedObjectUnits = new(
                importedSceneSource.ReadObjectUnitsAsync(cancellationToken),
                cityObjectCount => sourceCityObjectCount += cityObjectCount);
            SceneImportExecutionResult executionResult = await sceneSink.ExecuteAsync(
                executionPlan,
                countedObjectUnits.ReadAllAsync(cancellationToken),
                cancellationToken);

            cityObjectStopwatch.Stop();
            PlateauDiagnostics.Verbose(
                "Streamed {CityObjectCount} city objects in {ElapsedSeconds:F3}s.",
                sourceCityObjectCount,
                cityObjectStopwatch.Elapsed.TotalSeconds);
            PlateauDiagnostics.Verbose(
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
