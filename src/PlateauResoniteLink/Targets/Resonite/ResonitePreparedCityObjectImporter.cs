using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResonitePreparedCityObjectImporter
{
    Task ImportAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        LiveSendQueuedCityObject queuedCityObject,
        PreparedCityObject preparedCityObject,
        ResoniteLinkSendDiagnostics diagnostics,
        Action<string>? progressReporter,
        CancellationToken cancellationToken);
}

internal sealed class ResonitePreparedCityObjectImporter(
    IResoniteGeometryAssetPlanner geometryAssetPlanner,
    IResoniteSceneMaterialPlanComposer sceneMaterialPlanComposer,
    IResoniteBatchEmissionPlanner batchEmissionPlanner,
    IResoniteSceneBatchEmitter batchEmitter) : IResonitePreparedCityObjectImporter
{
    public async Task ImportAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        LiveSendQueuedCityObject queuedCityObject,
        PreparedCityObject preparedCityObject,
        ResoniteLinkSendDiagnostics diagnostics,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(routedClient);
        ArgumentNullException.ThrowIfNull(queuedCityObject);
        ArgumentNullException.ThrowIfNull(preparedCityObject);
        ArgumentNullException.ThrowIfNull(diagnostics);

        ResoniteConstructionCityObject cityObject = preparedCityObject.CityObject;
        using ResoniteLinkSendDiagnostics.CityObjectSendScope sendScope = diagnostics.BeginCityObjectSend(cityObject.PackageName);
        Stopwatch cityObjectStopwatch = Stopwatch.StartNew();
        ReportImportStep(progressReporter, cityObject, "Creating object slot hierarchy.");
        Stopwatch slotHierarchyStopwatch = Stopwatch.StartNew();
        ResoniteObjectSlotHierarchy objectSlots = await AwaitWithSlowCityObjectWarningAsync(
            queuedCityObject.ObjectHierarchyTask,
            cancellationToken);
        slotHierarchyStopwatch.Stop();
        using CancellationTokenSource importStepCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay =
            CreatePreparedTerrainTextureDataByOverlay(preparedCityObject);
        Task<ResoniteUploadedTextureAssetSet> uploadedTextureAssetsTask = ResonitePreparedTextureUploader.UploadAsync(
            state,
            routedClient,
            preparedCityObject,
            importStepCancellation.Token);
        Stopwatch geometryStopwatch = Stopwatch.StartNew();
        Task<PlannedGeometryAsset> geometryPlanningTask = geometryAssetPlanner.PlanAsync(
            routedClient,
            cityObject,
            preparedCityObject,
            preparedTerrainTextureDataByOverlay,
            progressReporter,
            importStepCancellation.Token);
        Stopwatch materialStopwatch = new();
        Task<PlannedSceneMaterialPlan>? materialPlanningTask = null;
        PlannedSceneMaterialPlan plannedMaterials;
        PlannedGeometryAsset plannedGeometryAsset;
        try
        {
            ResoniteUploadedTextureAssetSet uploadedTextureAssets = await uploadedTextureAssetsTask;
            materialStopwatch.Start();
            materialPlanningTask = sceneMaterialPlanComposer.ComposeAsync(
                state,
                routedClient,
                cityObject,
                uploadedTextureAssets.TextureUrisByPayload,
                uploadedTextureAssets.TerrainTextureUrisByOverlay,
                uploadedTextureAssets.TerrainTexturePropertyBlockComponentsByMeshCode,
                message => ReportImportStep(progressReporter, cityObject, message),
                importStepCancellation.Token);
            plannedMaterials = await materialPlanningTask;
            materialStopwatch.Stop();

            ReportImportStep(progressReporter, cityObject, $"Preparing geometry assets ({PreparedConstructionGeometryFormatter.Describe(preparedCityObject.Geometry)}).");
            plannedGeometryAsset = await geometryPlanningTask;
            geometryStopwatch.Stop();
        }
        catch
        {
            await importStepCancellation.CancelAsync();
            IEnumerable<Task> tasksToObserve = materialPlanningTask is null
                ? [uploadedTextureAssetsTask, geometryPlanningTask]
                : [uploadedTextureAssetsTask, materialPlanningTask, geometryPlanningTask];
            await ObserveTaskFailuresAsync(tasksToObserve);
            throw;
        }

        PlannedSceneObjectEmission emissionPlan = new(
            plannedGeometryAsset,
            plannedMaterials.MaterialAssets,
            new PlannedRenderer(
                plannedGeometryAsset.Identity,
                plannedMaterials.RendererMaterialBindings),
            new PlannedCollider(
                plannedGeometryAsset.Identity,
                cityObject.CollisionEnabled));
        PlannedBatchEmission batchEmission = batchEmissionPlanner.Create(objectSlots, emissionPlan);

        ReportImportStep(progressReporter, cityObject, "Creating object-scoped DataModel batch.");
        Stopwatch batchStopwatch = Stopwatch.StartNew();
        await batchEmitter.ExecuteAsync(
            routedClient,
            cityObject,
            batchEmission,
            progressReporter,
            cancellationToken);
        batchStopwatch.Stop();

        ReportImportStep(progressReporter, cityObject, "Live import completed.");
        cityObjectStopwatch.Stop();
        progressReporter?.Invoke(
            PlateauLog.Debug(
                "live",
                $"City object '{cityObject.DisplayName}' phase timings: "
                + $"slot_hierarchy_s={slotHierarchyStopwatch.Elapsed.TotalSeconds:F3} "
                + $"geometry_assets_s={geometryStopwatch.Elapsed.TotalSeconds:F3} "
                + $"materials_s={materialStopwatch.Elapsed.TotalSeconds:F3} "
                + $"batch_s={batchStopwatch.Elapsed.TotalSeconds:F3} "
                + $"total_send_s={cityObjectStopwatch.Elapsed.TotalSeconds:F3}."));
        sendScope.MarkSent();
        if (Interlocked.CompareExchange(ref state.Progress.FirstImportedCityObjectLogged, 1, 0) == 0)
        {
            progressReporter?.Invoke(
                PlateauLog.Debug(
                    "live",
                    $"First city object imported after {state.Runtime.ElapsedTotalSeconds:F3}s: "
                    + $"{cityObject.DisplayName} ({cityObject.PackageName}/{cityObject.SlotKey})"));
        }
    }

    private static Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> CreatePreparedTerrainTextureDataByOverlay(
        PreparedCityObject preparedCityObject)
    {
        Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> generatedTerrainTexturesByOverlay = [];
        foreach (PreparedTextureReference texture in preparedCityObject.Textures)
        {
            if (texture is { TerrainOverlay: not null, GeneratedTerrainTexture: not null })
            {
                generatedTerrainTexturesByOverlay.TryAdd(texture.TerrainOverlay, texture.GeneratedTerrainTexture);
            }
        }

        return generatedTerrainTexturesByOverlay;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Best-effort cleanup should observe and suppress orphaned import task failures after the primary send failure.")]
    private static async Task ObserveTaskFailureAsync(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
        }
    }

    private static Task ObserveTaskFailuresAsync(IEnumerable<Task> tasks)
    {
        return Task.WhenAll(tasks.Select(ObserveTaskFailureAsync));
    }

    private static Task<T> AwaitWithSlowCityObjectWarningAsync<T>(
        Task<T> operationTask,
        CancellationToken cancellationToken)
    {
        return operationTask.WaitAsync(cancellationToken);
    }

    private static void ReportImportStep(
        Action<string>? progressReporter,
        ResoniteConstructionCityObject cityObject,
        string step)
    {
        progressReporter?.Invoke(
            PlateauLog.Debug(
                "live",
                $"Importing '{cityObject.DisplayName}' ({cityObject.PackageName}/{cityObject.SlotKey}): {step}"));
    }
}
