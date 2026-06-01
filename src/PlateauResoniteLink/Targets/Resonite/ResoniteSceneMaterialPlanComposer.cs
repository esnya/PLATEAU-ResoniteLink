using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteSceneMaterialPlanComposer
{
    Task<PlannedSceneMaterialPlan> ComposeAsync(
        LiveSendRunState state,
        IResoniteLinkClient importClient,
        ResoniteConstructionCityObject cityObject,
        IReadOnlyDictionary<ResoniteTexturePayload, Uri> preparedTextureUrisByPayload,
        IReadOnlyDictionary<TerrainTextureOverlay, Uri> preparedTerrainTextureUrisByOverlay,
        IReadOnlyDictionary<ThirdRegionalMeshCode, ResoniteComponentLocator> preparedTerrainTexturePropertyBlockComponentsByMeshCode,
        Action<string> reportMaterialStep,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteSceneMaterialPlanComposer(IResoniteMaterialPlanning materialPlanning) : IResoniteSceneMaterialPlanComposer
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
            ResoniteMaterialBinding material = ResolveTerrainTextureMaterialForEmission(
                cityObject,
                cityObject.Materials[materialIndex]);
            reportMaterialStep($"Creating material {materialIndex + 1}/{cityObject.Materials.Count}.");
            if (material.AssetBinding.IsSharedCommon
                && material.CommonMaterial is not null)
            {
                materialPlanTasks[materialIndex] = PlanSharedCommonRendererMaterialAsync(
                    state,
                    material,
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
        IReadOnlyDictionary<ResoniteTexturePayload, Uri> preparedTextureUrisByPayload,
        IReadOnlyDictionary<TerrainTextureOverlay, Uri> preparedTerrainTextureUrisByOverlay,
        IReadOnlyDictionary<ThirdRegionalMeshCode, ResoniteComponentLocator> terrainTexturePropertyBlockComponentsByMeshCode,
        CancellationToken cancellationToken)
    {
        DefaultCommonMaterialMember member = sourceMaterial.CommonMaterial
            ?? throw new InvalidOperationException("Common renderer material requires a typed common material member.");
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

}
