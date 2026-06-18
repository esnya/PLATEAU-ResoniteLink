using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


using PlateauResoniteLink.Diagnostics;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

using PlateauResoniteLink.Core;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class ResonitePreparedCityObjectImporter(
    ResoniteSceneMaterialPlanComposer sceneMaterialPlanComposer)
{
    public async Task ImportAsync(
        LiveSendRunState state,
        IResoniteLinkClient routedClient,
        LiveSendQueuedCityObject queuedCityObject,
        PreparedCityObject preparedCityObject,
        ResoniteLinkSendDiagnostics diagnostics,
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
        ReportImportStep(cityObject, "Creating object slot hierarchy.");
        Stopwatch slotHierarchyStopwatch = Stopwatch.StartNew();
        ResoniteObjectSlotHierarchy objectSlots = await AwaitWithCancellationAsync(
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
        Task<PlannedGeometryAsset> geometryPlanningTask = ResoniteGeometryAssetPlanner.PlanAsync(
            routedClient,
            cityObject,
            preparedCityObject,
            preparedTerrainTextureDataByOverlay,
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
                message => ReportImportStep(cityObject, message),
                importStepCancellation.Token);
            plannedMaterials = await materialPlanningTask;
            materialStopwatch.Stop();

            ReportImportStep(cityObject, $"Preparing geometry assets ({PreparedConstructionGeometryFormatter.Describe(preparedCityObject.Geometry)}).");
            plannedGeometryAsset = await geometryPlanningTask;
            geometryStopwatch.Stop();
        }
        catch
        {
            IEnumerable<Task> tasksToObserve = materialPlanningTask is null
                ? [uploadedTextureAssetsTask, geometryPlanningTask]
                : [uploadedTextureAssetsTask, materialPlanningTask, geometryPlanningTask];
            await ResoniteImportStepTaskCleanup.CancelAndObserveFailuresAsync(
                importStepCancellation,
                tasksToObserve);
            throw;
        }

        PlannedBatchEmission batchEmission = ResoniteBatchEmissionPlanner.Create(
            objectSlots,
            plannedGeometryAsset,
            plannedMaterials.MaterialAssets,
            plannedMaterials.RendererMaterialBindings,
            cityObject.CollisionEnabled);

        ReportImportStep(cityObject, "Creating object-scoped DataModel batch.");
        Stopwatch batchStopwatch = Stopwatch.StartNew();
        await PlannedBatchEmissionInterpreter.ExecuteAsync(
            routedClient,
            cityObject,
            batchEmission,
            cancellationToken);
        batchStopwatch.Stop();

        ReportImportStep(cityObject, "Live import completed.");
        if (state.Context.Plan.DistanceCullingEnabled)
        {
            state.DistanceCulling.Register(cityObject, objectSlots);
        }

        cityObjectStopwatch.Stop();
        PlateauDiagnostics.Verbose(
            "City object '{DisplayName}' phase timings: slot_hierarchy_s={SlotHierarchySeconds:F3} geometry_assets_s={GeometryAssetsSeconds:F3} materials_s={MaterialsSeconds:F3} batch_s={BatchSeconds:F3} total_send_s={TotalSendSeconds:F3}.",
            cityObject.DisplayName,
            slotHierarchyStopwatch.Elapsed.TotalSeconds,
            geometryStopwatch.Elapsed.TotalSeconds,
            materialStopwatch.Elapsed.TotalSeconds,
            batchStopwatch.Elapsed.TotalSeconds,
            cityObjectStopwatch.Elapsed.TotalSeconds);
        sendScope.MarkSent();
        if (Interlocked.CompareExchange(ref state.Progress.FirstImportedCityObjectLogged, 1, 0) == 0)
        {
            PlateauDiagnostics.Verbose(
                "First city object imported after {ElapsedSeconds:F3}s: {DisplayName} ({PackageName}/{SlotKey})",
                state.Runtime.ElapsedTotalSeconds,
                cityObject.DisplayName,
                cityObject.PackageName,
                cityObject.SlotKey);
        }
    }

    private static Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> CreatePreparedTerrainTextureDataByOverlay(
        PreparedCityObject preparedCityObject)
    {
        Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> generatedTerrainTexturesByOverlay = [];
        foreach (PreparedTerrainOverlayTextureReference texture in preparedCityObject.Textures.OfType<PreparedTerrainOverlayTextureReference>())
        {
            generatedTerrainTexturesByOverlay.TryAdd(texture.Overlay, texture.GeneratedTerrainTexture);
        }

        return generatedTerrainTexturesByOverlay;
    }

    private static Task<T> AwaitWithCancellationAsync<T>(
        Task<T> operationTask,
        CancellationToken cancellationToken)
    {
        return operationTask.WaitAsync(cancellationToken);
    }

    private static void ReportImportStep(
        ResoniteConstructionCityObject cityObject,
        string step)
    {
        PlateauDiagnostics.Verbose(
            "Importing '{DisplayName}' ({PackageName}/{SlotKey}): {Step}",
            cityObject.DisplayName,
            cityObject.PackageName,
            cityObject.SlotKey,
            step);
    }
}
