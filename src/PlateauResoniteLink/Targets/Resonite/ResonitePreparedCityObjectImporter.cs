using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class ResonitePreparedCityObjectImporter(
    ResoniteMaterialPlanning materialPlanning)
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
            materialPlanningTask = ComposeMaterialPlanAsync(
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

        ReportImportStep(progressReporter, cityObject, "Creating object-scoped DataModel batch.");
        Stopwatch batchStopwatch = Stopwatch.StartNew();
        await PlannedBatchEmissionInterpreter.ExecuteAsync(
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
        foreach (PreparedTerrainOverlayTextureReference texture in preparedCityObject.Textures.OfType<PreparedTerrainOverlayTextureReference>())
        {
            generatedTerrainTexturesByOverlay.TryAdd(texture.Overlay, texture.GeneratedTerrainTexture);
        }

        return generatedTerrainTexturesByOverlay;
    }

    private async Task<PlannedSceneMaterialPlan> ComposeMaterialPlanAsync(
        LiveSendRunState state,
        IResoniteLinkClient importClient,
        ResoniteConstructionCityObject cityObject,
        IReadOnlyDictionary<ResoniteTexturePayload, Uri> preparedTextureUrisByPayload,
        IReadOnlyDictionary<TerrainTextureOverlay, Uri> preparedTerrainTextureUrisByOverlay,
        IReadOnlyDictionary<ThirdRegionalMeshCode, ResoniteComponentLocator> preparedTerrainTexturePropertyBlockComponentsByMeshCode,
        Action<string> reportMaterialStep,
        CancellationToken cancellationToken)
    {
        Task<(PlannedMaterialAsset MaterialAsset, PlannedRendererMaterialBinding RendererBinding)>[] materialPlanTasks
            = new Task<(PlannedMaterialAsset MaterialAsset, PlannedRendererMaterialBinding RendererBinding)>[cityObject.Materials.Count];
        for (int materialIndex = 0; materialIndex < cityObject.Materials.Count; materialIndex++)
        {
            ResoniteMaterialBinding material = ResolveTerrainTextureMaterialForEmission(
                cityObject,
                cityObject.Materials[materialIndex]);
            material = ResoniteTerrainOverlayMaterialContract.ValidateMaterial(cityObject, materialIndex, material);
            reportMaterialStep($"Creating material {materialIndex + 1}/{cityObject.Materials.Count}.");
            if (material.CommonMaterial is { } commonMaterial)
            {
                materialPlanTasks[materialIndex] = PlanSharedCommonRendererMaterialAsync(
                    state,
                    material,
                    commonMaterial,
                    preparedTextureUrisByPayload,
                    preparedTerrainTextureUrisByOverlay,
                    preparedTerrainTexturePropertyBlockComponentsByMeshCode,
                    cancellationToken);
                continue;
            }

            materialPlanTasks[materialIndex] = PlanDedicatedRendererMaterialAsync(
                importClient,
                material,
                materialIndex,
                preparedTextureUrisByPayload,
                preparedTerrainTextureUrisByOverlay,
                preparedTerrainTexturePropertyBlockComponentsByMeshCode,
                preserveDedicatedMaterialSlot: ResonitePackageSemantics.IsDemPackage(cityObject.PackageName),
                cancellationToken);
        }

        (PlannedMaterialAsset MaterialAsset, PlannedRendererMaterialBinding RendererBinding)[] materialPlans = await Task.WhenAll(materialPlanTasks);
        return new PlannedSceneMaterialPlan(
            materialPlans.Select(static plan => plan.MaterialAsset).ToArray(),
            materialPlans.Select(static plan => plan.RendererBinding).ToArray());
    }

    private static async Task<(PlannedMaterialAsset MaterialAsset, PlannedRendererMaterialBinding RendererBinding)> PlanSharedCommonRendererMaterialAsync(
        LiveSendRunState runState,
        ResoniteMaterialBinding sourceMaterial,
        DefaultCommonMaterialMember member,
        IReadOnlyDictionary<ResoniteTexturePayload, Uri> preparedTextureUrisByPayload,
        IReadOnlyDictionary<TerrainTextureOverlay, Uri> preparedTerrainTextureUrisByOverlay,
        IReadOnlyDictionary<ThirdRegionalMeshCode, ResoniteComponentLocator> terrainTexturePropertyBlockComponentsByMeshCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(member);

        string familySlotName = ResoniteSceneMaterialConventions.GetCommonMaterialFamilySlotName(
            SceneImportContractMapper.ToInternal(member.CreateBinding([0])));
        if (runState.Materials.CommonMaterialFamilyWarmupTasks.TryGetValue(familySlotName, out Task? familyWarmupTask))
        {
            await familyWarmupTask.WaitAsync(cancellationToken);
        }

        if (!runState.Materials.CommonMaterialAssets.TryGetAsset(member, out CreatedMaterialAsset existingMaterialAsset))
        {
            throw new InvalidOperationException(
                $"Setup did not resolve common material ({ResoniteMaterialComponentPolicy.DescribeForDiagnostics(sourceMaterial)}) before runtime emission.");
        }
        PlannedReusableMaterialAsset sharedMaterialAsset = new(existingMaterialAsset.MaterialComponent);
        PlannedTextureAsset? mainTextureOverride = ResoniteMaterialPlanning.PlanMainTextureOverride(
            sourceMaterial,
            preparedTextureUrisByPayload,
            preparedTerrainTextureUrisByOverlay);
        PlannedRendererMaterialBinding rendererBinding = mainTextureOverride is null
            ? new PlannedDirectRendererMaterialBinding(sharedMaterialAsset)
            : CreateMainTextureOverrideRendererBinding(
                sharedMaterialAsset,
                mainTextureOverride,
                terrainTexturePropertyBlockComponentsByMeshCode,
                sourceMaterial);
        return (sharedMaterialAsset, rendererBinding);
    }

    private async Task<(PlannedMaterialAsset MaterialAsset, PlannedRendererMaterialBinding RendererBinding)> PlanDedicatedRendererMaterialAsync(
        IResoniteLinkClient client,
        ResoniteMaterialBinding sourceMaterial,
        int materialIndex,
        IReadOnlyDictionary<ResoniteTexturePayload, Uri> preparedTextureUrisByPayload,
        IReadOnlyDictionary<TerrainTextureOverlay, Uri> preparedTerrainTextureUrisByOverlay,
        IReadOnlyDictionary<ThirdRegionalMeshCode, ResoniteComponentLocator> preparedTerrainTexturePropertyBlockComponentsByMeshCode,
        bool preserveDedicatedMaterialSlot,
        CancellationToken cancellationToken)
    {
        ResoniteMaterialBinding materialComponentSource = sourceMaterial.TerrainOverlay is null
            ? sourceMaterial
            : sourceMaterial with
            {
                TerrainOverlayMaterial = null,
                TextureScale = null,
                TextureOffset = null,
            };
        PlannedDedicatedMaterialAsset plannedMaterial = await materialPlanning.PlanDedicatedMaterialAssetAsync(
            client,
            materialComponentSource,
            materialIndex,
            preparedTextureUrisByPayload,
            preparedTerrainTextureUrisByOverlay,
            preserveDedicatedMaterialSlot,
            cancellationToken);
        if (sourceMaterial.TerrainOverlay is not null)
        {
            PlannedTextureAsset? mainTextureOverride = ResoniteMaterialPlanning.PlanMainTextureOverride(
                sourceMaterial,
                preparedTextureUrisByPayload,
                preparedTerrainTextureUrisByOverlay);
            if (mainTextureOverride is not null)
            {
                return (
                    plannedMaterial,
                    CreateMainTextureOverrideRendererBinding(
                        plannedMaterial,
                        mainTextureOverride,
                        preparedTerrainTexturePropertyBlockComponentsByMeshCode,
                        sourceMaterial));
            }
        }

        return (plannedMaterial, new PlannedDirectRendererMaterialBinding(plannedMaterial));
    }

    private static ResoniteMaterialBinding ResolveTerrainTextureMaterialForEmission(
        ResoniteConstructionCityObject cityObject,
        ResoniteMaterialBinding material)
    {
        return (cityObject.Geometry is ResoniteTerrainGridGeometry
                || cityObject.Geometry is ResoniteDynamicTerrainGeometry)
            && material.TerrainOverlay is not null
            ? material with
            {
                TextureScale = null,
                TextureOffset = null,
            }
            : material;
    }

    private static PlannedMainTextureOverrideRendererMaterialBinding CreateMainTextureOverrideRendererBinding(
        PlannedMaterialAsset materialAsset,
        PlannedTextureAsset mainTexture,
        IReadOnlyDictionary<ThirdRegionalMeshCode, ResoniteComponentLocator> terrainTexturePropertyBlockComponentsByMeshCode,
        ResoniteMaterialBinding sourceMaterial)
    {
        if (sourceMaterial.TerrainOverlay is null)
        {
            return new PlannedAlbedoMainTextureOverrideRendererMaterialBinding(materialAsset, mainTexture);
        }

        return new PlannedTerrainMainTextureOverrideRendererMaterialBinding(
            materialAsset,
            mainTexture,
            null,
            sourceMaterial.TerrainOverlayMaterial is not null
            && terrainTexturePropertyBlockComponentsByMeshCode.TryGetValue(sourceMaterial.TerrainOverlayMaterial.MeshCode, out ResoniteComponentLocator propertyBlockComponent)
                ? propertyBlockComponent
                : null);
    }

    private static Task<T> AwaitWithCancellationAsync<T>(
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
