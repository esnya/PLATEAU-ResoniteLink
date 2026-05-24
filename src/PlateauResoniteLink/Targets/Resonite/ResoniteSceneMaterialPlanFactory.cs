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

internal interface IResoniteSceneMaterialPlanFactory
{
    Task<PlannedSceneMaterialPlan> CreateAsync(
        LiveSendRunState state,
        IResoniteLinkClient importClient,
        ResoniteConstructionCityObject cityObject,
        UploadedTextureAssetSet uploadedTextures,
        Action<string>? reportStep,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteSceneMaterialPlanFactory(
    IResoniteMaterialPlanning materialPlanning) : IResoniteSceneMaterialPlanFactory
{
    private const string DemPackageName = "dem";
    private readonly IResoniteMaterialPlanning materialPlanning =
        materialPlanning ?? throw new ArgumentNullException(nameof(materialPlanning));

    public async Task<PlannedSceneMaterialPlan> CreateAsync(
        LiveSendRunState state,
        IResoniteLinkClient importClient,
        ResoniteConstructionCityObject cityObject,
        UploadedTextureAssetSet uploadedTextures,
        Action<string>? reportStep,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(importClient);
        ArgumentNullException.ThrowIfNull(cityObject);
        ArgumentNullException.ThrowIfNull(uploadedTextures);

        Task<(PlannedMaterialAsset MaterialAsset, PlannedRendererMaterialBinding RendererBinding)>[] materialPlanTasks
            = new Task<(PlannedMaterialAsset MaterialAsset, PlannedRendererMaterialBinding RendererBinding)>[cityObject.Materials.Count];
        for (int materialIndex = 0; materialIndex < cityObject.Materials.Count; materialIndex++)
        {
            ResoniteMaterialBinding material = ResolveTerrainTextureMaterialForEmission(
                cityObject,
                cityObject.Materials[materialIndex]);
            material = ResoniteTerrainTextureMaterialContract.ValidateForEmission(cityObject, materialIndex, material);
            reportStep?.Invoke($"Creating material {materialIndex + 1}/{cityObject.Materials.Count}.");
            if (material.CommonMaterial is not null)
            {
                materialPlanTasks[materialIndex] = PlanSharedCommonRendererMaterialAsync(
                    state,
                    material,
                    uploadedTextures.TextureUrisByPayload,
                    uploadedTextures.TerrainTextureUrisByOverlay,
                    uploadedTextures.TerrainTexturePropertyBlockComponentsByMeshCode,
                    cancellationToken);
                continue;
            }

            materialPlanTasks[materialIndex] = PlanDedicatedRendererMaterialAsync(
                importClient,
                material,
                materialIndex,
                cityObject.PackageName,
                uploadedTextures.TextureUrisByPayload,
                uploadedTextures.TerrainTextureUrisByOverlay,
                uploadedTextures.TerrainTexturePropertyBlockComponentsByMeshCode,
                preserveDedicatedMaterialSlot: IsDemPackage(cityObject.PackageName),
                cancellationToken);
        }

        (PlannedMaterialAsset MaterialAsset, PlannedRendererMaterialBinding RendererBinding)[] materialPlans = await Task.WhenAll(materialPlanTasks);
        return new PlannedSceneMaterialPlan(
            materialPlans.Select(static plan => plan.MaterialAsset).ToArray(),
            materialPlans.Select(static plan => plan.RendererBinding).ToArray());
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

    private static async Task<(PlannedMaterialAsset MaterialAsset, PlannedRendererMaterialBinding RendererBinding)> PlanSharedCommonRendererMaterialAsync(
        LiveSendRunState runState,
        ResoniteMaterialBinding sourceMaterial,
        IReadOnlyDictionary<ResoniteTexturePayload, Uri> preparedTextureUrisByPayload,
        IReadOnlyDictionary<TerrainTextureOverlay, Uri> preparedTerrainTextureUrisByOverlay,
        IReadOnlyDictionary<string, ResoniteComponentLocator> terrainTexturePropertyBlockComponentsByMeshCode,
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

        PlannedReusableMaterialAsset sharedMaterialAsset = new(
            existingMaterialAsset.MaterialComponent);
        PlannedTextureAsset? mainTextureOverride = await ResoniteMaterialPlanning.PlanMainTextureOverrideAsync(
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
        string packageName,
        IReadOnlyDictionary<ResoniteTexturePayload, Uri> preparedTextureUrisByPayload,
        IReadOnlyDictionary<TerrainTextureOverlay, Uri> preparedTerrainTextureUrisByOverlay,
        IReadOnlyDictionary<string, ResoniteComponentLocator> terrainTexturePropertyBlockComponentsByMeshCode,
        bool preserveDedicatedMaterialSlot,
        CancellationToken cancellationToken)
    {
        ResoniteMaterialBinding materialComponentSource = sourceMaterial.TerrainOverlay is null
            ? sourceMaterial
            : sourceMaterial with
            {
                TerrainOverlay = null,
                TerrainMeshCode = null,
                TextureScale = null,
                TextureOffset = null,
            };
        PlannedDedicatedMaterialAsset plannedMaterial = await materialPlanning.PlanDedicatedMaterialAssetAsync(
            client,
            materialComponentSource,
            materialIndex,
            packageName,
            preparedTextureUrisByPayload,
            preparedTerrainTextureUrisByOverlay,
            preserveDedicatedMaterialSlot,
            cancellationToken);
        if (sourceMaterial.TerrainOverlay is not null)
        {
            PlannedTextureAsset? mainTextureOverride = await ResoniteMaterialPlanning.PlanMainTextureOverrideAsync(
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
                        terrainTexturePropertyBlockComponentsByMeshCode,
                        sourceMaterial));
            }
        }

        return (plannedMaterial, new PlannedDirectRendererMaterialBinding(plannedMaterial));
    }

    private static PlannedMainTextureOverrideRendererMaterialBinding CreateMainTextureOverrideRendererBinding(
        PlannedMaterialAsset materialAsset,
        PlannedTextureAsset mainTexture,
        IReadOnlyDictionary<string, ResoniteComponentLocator> terrainTexturePropertyBlockComponentsByMeshCode,
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
            sourceMaterial.TerrainMeshCode is not null
            && terrainTexturePropertyBlockComponentsByMeshCode.TryGetValue(sourceMaterial.TerrainMeshCode, out ResoniteComponentLocator propertyBlockComponent)
                ? propertyBlockComponent
                : null);
    }

    private static bool IsDemPackage(string packageName)
    {
        return string.Equals(packageName, DemPackageName, StringComparison.OrdinalIgnoreCase);
    }
}
