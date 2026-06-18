using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Core.Domain.Importing;
using PlateauResoniteLink.Resonite.Targets.Resonite.Execution;
using PlateauResoniteLink.Resonite.Transport.ResoniteLink;
using PlateauResoniteLink.Core.Application.Importing.Contracts;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal sealed class ResoniteSceneMaterialPlanComposer(IResoniteMaterialPlanning materialPlanning)
{
    public async Task<PlannedSceneMaterialPlan> ComposeAsync(
        LiveSendRunState state,
        IResoniteLinkClient importClient,
        ResoniteConstructionCityObject cityObject,
        IReadOnlyDictionary<ResoniteTexturePayload, Uri> preparedTextureUrisByPayload,
        IReadOnlyDictionary<TerrainTextureOverlay, Uri> preparedTerrainTextureUrisByOverlay,
        IReadOnlyDictionary<ThirdRegionalMeshCode, ResoniteComponentLocator> preparedTerrainTexturePropertyBlockComponentsByMeshCode,
        Action<string> reportMaterialStep,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(importClient);
        ArgumentNullException.ThrowIfNull(cityObject);
        ArgumentNullException.ThrowIfNull(preparedTextureUrisByPayload);
        ArgumentNullException.ThrowIfNull(preparedTerrainTextureUrisByOverlay);
        ArgumentNullException.ThrowIfNull(preparedTerrainTexturePropertyBlockComponentsByMeshCode);
        ArgumentNullException.ThrowIfNull(reportMaterialStep);

        Task<(PlannedMaterialAsset MaterialAsset, PlannedRendererMaterialBinding RendererBinding)>[] materialPlanTasks
            = new Task<(PlannedMaterialAsset MaterialAsset, PlannedRendererMaterialBinding RendererBinding)>[cityObject.Materials.Count];
        for (int materialIndex = 0; materialIndex < cityObject.Materials.Count; materialIndex++)
        {
            ResoniteMaterialBinding material = ResoniteSceneMaterialEmissionNormalizer.NormalizeTerrainTextureMaterialForEmission(
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
                preparedTextureUrisByPayload,
                preparedTerrainTextureUrisByOverlay,
                preparedTerrainTexturePropertyBlockComponentsByMeshCode,
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
        LocalRendererOverrideTextureProvider? mainTextureOverride = ResoniteMaterialPlanning.PlanMainTextureOverride(
            sourceMaterial,
            preparedTextureUrisByPayload,
            preparedTerrainTextureUrisByOverlay);
        PlannedRendererMaterialBinding rendererBinding = mainTextureOverride is null
            ? new PlannedDirectRendererMaterialBinding(sharedMaterialAsset)
            : ResoniteRendererMaterialBindingPlanner.CreateMainTextureOverrideRendererBinding(
                sharedMaterialAsset,
                mainTextureOverride,
                terrainTexturePropertyBlockComponentsByMeshCode,
                sourceMaterial);
        return (sharedMaterialAsset, rendererBinding);
    }

    private async Task<(PlannedMaterialAsset MaterialAsset, PlannedRendererMaterialBinding RendererBinding)> PlanDedicatedRendererMaterialAsync(
        IResoniteLinkClient client,
        ResoniteMaterialBinding sourceMaterial,
        IReadOnlyDictionary<ResoniteTexturePayload, Uri> preparedTextureUrisByPayload,
        IReadOnlyDictionary<TerrainTextureOverlay, Uri> preparedTerrainTextureUrisByOverlay,
        IReadOnlyDictionary<ThirdRegionalMeshCode, ResoniteComponentLocator> preparedTerrainTexturePropertyBlockComponentsByMeshCode,
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
            preparedTextureUrisByPayload,
            preparedTerrainTextureUrisByOverlay,
            cancellationToken);
        if (sourceMaterial.TerrainOverlay is not null)
        {
            LocalRendererOverrideTextureProvider? mainTextureOverride = ResoniteMaterialPlanning.PlanMainTextureOverride(
                sourceMaterial,
                preparedTextureUrisByPayload,
                preparedTerrainTextureUrisByOverlay);
            if (mainTextureOverride is not null)
            {
                return (
                    plannedMaterial,
                    ResoniteRendererMaterialBindingPlanner.CreateMainTextureOverrideRendererBinding(
                        plannedMaterial,
                        mainTextureOverride,
                        preparedTerrainTexturePropertyBlockComponentsByMeshCode,
                        sourceMaterial));
            }
        }

        return (plannedMaterial, new PlannedDirectRendererMaterialBinding(plannedMaterial));
    }
}
