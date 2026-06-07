using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using PlateauResoniteLink.Diagnostics;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class ResoniteCommonMaterialSetupPreparer(
    IResoniteMaterialPlanning materialPlanning)
{
    public async Task PrepareAsync(
        IResoniteLinkClient client,
        ResoniteSceneSetupState setupState,
        CommonMaterialAssetCache materials,
        CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        CommonMaterialCatalog<ResoniteCommonMaterialPlan> commonMaterialPlans =
            ResoniteCommonMaterialPlans.CreateCatalogPlans(commonMaterials);
        if (commonMaterialPlans.Count == 0)
        {
            logger.WriteInformation("No common material assets are required during scene setup.");
            return;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        int preparedCount = 0;
        logger.WriteInformation(
            "Preparing {CommonMaterialAssetCount} common material assets during scene setup before object streaming.",
            commonMaterialPlans.Count);
        ResoniteBatchOperations.BatchActionBuilder batchBuilder = new();
        List<PreparedCommonMaterialBatchEntry> preparedMaterials = [];
        foreach (CommonMaterialCatalogMember<ResoniteCommonMaterialPlan> catalogMember in commonMaterialPlans.EnumerateMembers())
        {
            ResoniteCommonMaterialPlan materialPlan = catalogMember.Item;
            cancellationToken.ThrowIfCancellationRequested();
            ResoniteMaterialBinding material = materialPlan.Material;
            string familySlotName = ResoniteSceneMaterialConventions.GetCommonMaterialFamilySlotName(material);
            string materialSlotName = materialPlan.SlotName;
            if (!setupState.CommonMaterialFamilies.Contains(familySlotName))
            {
                throw new InvalidOperationException(
                    $"Setup did not create common material family '{familySlotName}' before common asset preparation.");
            }

            if (materials.CommonMaterialAssets.TryGetAsset(materialPlan.Member, out _))
            {
                preparedCount++;
                continue;
            }

            Stopwatch materialStopwatch = Stopwatch.StartNew();
            logger.WriteInformation(
                "Preparing common material asset {PreparedCount}/{TotalCount}: family='{FamilySlotName}', slot='{MaterialSlotName}'.",
                preparedCount + 1,
                commonMaterialPlans.Count,
                familySlotName,
                materialSlotName);
            CreatedSlot familySlot = await FindRequiredCommonMaterialFamilySlotAsync(
                client,
                setupState.CommonAssetsRootSlot,
                familySlotName,
                cancellationToken);
            (CreatedSlot? reusableSlot, ResoniteComponentLocator? existingComponent) = await TryFindReusableCommonMaterialSlotAsync(
                client,
                familySlot,
                ResoniteSceneMaterialConventions.CreateCommonMaterialSlotLookupNames(material),
                ResoniteMaterialComponentPolicy.GetComponentType(material),
                cancellationToken);
            if (existingComponent is not null)
            {
                materials.CommonMaterialAssets.Set(new ResoniteCommonMaterialAsset(
                    materialPlan.Member,
                    material,
                    new CreatedMaterialAsset(existingComponent.Value, null)));
                preparedCount++;
                logger.WriteInformation(
                    "Reused common material asset {PreparedCount}/{TotalCount}: family='{FamilySlotName}', slot='{MaterialSlotName}', elapsed_s={ElapsedSeconds:F2}.",
                    preparedCount,
                    commonMaterialPlans.Count,
                    familySlotName,
                    materialSlotName,
                    materialStopwatch.Elapsed.TotalSeconds);
                continue;
            }

            PlannedDedicatedMaterialAsset plannedMaterial = await materialPlanning.PlanCommonMaterialAssetAsync(
                client,
                material,
                materials.BundledTextureImportTasks,
                cancellationToken);
            string materialContainerSlotId;
            ResoniteBatchOperations.PendingBatchSlot? pendingMaterialSlot = null;
            if (reusableSlot is null)
            {
                pendingMaterialSlot = batchBuilder.AddSlot(
                    familySlot.Locator.Value,
                    materialSlotName,
                    null,
                    null);
                materialContainerSlotId = pendingMaterialSlot.Value.LocalId.Value;
            }
            else
            {
                materialContainerSlotId = reusableSlot.Value.Locator.Value;
            }

            ResoniteBatchOperations.PendingBatchComponent pendingMaterialComponent =
                ResoniteMaterialPlanning.AddCommonMaterialComponents(
                    batchBuilder,
                    plannedMaterial,
                    materialContainerSlotId);
            preparedMaterials.Add(new PreparedCommonMaterialBatchEntry(
                materialPlan,
                material,
                pendingMaterialSlot,
                pendingMaterialComponent));
            preparedCount++;
            logger.WriteInformation(
                "Planned common material asset {PreparedCount}/{TotalCount}: family='{FamilySlotName}', slot='{MaterialSlotName}', texture_import_elapsed_s={ElapsedSeconds:F2}.",
                preparedCount,
                commonMaterialPlans.Count,
                familySlotName,
                materialSlotName,
                materialStopwatch.Elapsed.TotalSeconds);
        }

        if (batchBuilder.Actions.Count > 0)
        {
            BatchResponse response = await client.RunDataModelOperationBatchAsync(batchBuilder.Actions, cancellationToken);
            CanonicalBatchEntityMap entityMap = CanonicalBatchEntityMap.Create(response);
            foreach (PreparedCommonMaterialBatchEntry preparedMaterial in preparedMaterials)
            {
                if (preparedMaterial.PendingMaterialSlot is not null)
                {
                    _ = entityMap.ResolveSlot(preparedMaterial.PendingMaterialSlot.Value);
                }

                CreatedComponent createdMaterialComponent = entityMap.ResolveComponent(preparedMaterial.PendingMaterialComponent);
                materials.CommonMaterialAssets.Set(new ResoniteCommonMaterialAsset(
                    preparedMaterial.MaterialPlan.Member,
                    preparedMaterial.Material,
                    new CreatedMaterialAsset(createdMaterialComponent.Locator, null)));
            }

            logger.WriteInformation(
                "Created {PreparedMaterialCount} common material assets in one setup component batch.",
                preparedMaterials.Count);
        }

        foreach (string family in setupState.CommonMaterialFamilies)
        {
            materials.CommonMaterialFamilyWarmupTasks[family] = Task.CompletedTask;
        }

        logger.WriteInformation(
            "Prepared {PreparedCount} common material assets during scene setup in {ElapsedSeconds:F2}s.",
            preparedCount,
            stopwatch.Elapsed.TotalSeconds);
    }

    private static async Task<CreatedSlot> FindRequiredCommonMaterialFamilySlotAsync(
        IResoniteLinkClient client,
        CreatedSlot commonAssetsRootSlot,
        string familySlotName,
        CancellationToken cancellationToken)
    {
        Slot? commonRootSnapshot = await client.GetSlotAsync(
            new ResoniteTransportSlotLocator(commonAssetsRootSlot.Locator.Value),
            2,
            cancellationToken);
        if (commonRootSnapshot is not null)
        {
            ResoniteSceneChildLookupResult lookup = new ResoniteSceneSlotSnapshot(commonRootSnapshot)
                .GetUniqueChildLookupResult(familySlotName, commonAssetsRootSlot.Locator.Value);
            if (lookup.State == ResoniteSceneChildLookupState.FoundWithId && lookup.Slot is not null)
            {
                return new CreatedSlot(
                    new ResoniteSlotLocator(lookup.Slot.ID ?? throw new InvalidOperationException("Common material family slot did not expose an ID.")),
                    lookup.Slot.Name?.Value ?? familySlotName);
            }
        }

        throw new InvalidOperationException(
            $"Setup did not create common material family slot '{familySlotName}' before runtime emission.");
    }

    private static async Task<(CreatedSlot? ReusableSlot, ResoniteComponentLocator? ExistingComponent)> TryFindReusableCommonMaterialSlotAsync(
        IResoniteLinkClient client,
        CreatedSlot familySlot,
        IReadOnlyList<string> lookupNames,
        string materialComponentType,
        CancellationToken cancellationToken)
    {
        if (lookupNames.Count == 0)
        {
            return (null, null);
        }

        Slot? familySlotSnapshot = await client.GetSlotAsync(new ResoniteTransportSlotLocator(familySlot.Locator.Value), 1, cancellationToken);
        if (familySlotSnapshot is null)
        {
            return (null, null);
        }

        ResoniteSceneSlotSnapshot familySlotView = new(familySlotSnapshot);
        CreatedSlot? reusableSlotWithoutComponent = null;
        foreach (string materialSlotName in lookupNames.Where(static name => !string.IsNullOrWhiteSpace(name)))
        {
            ResoniteSceneChildLookupResult materialLookup = familySlotView.GetUniqueChildLookupResult(materialSlotName, familySlot.Locator.Value);
            if (materialLookup.State != ResoniteSceneChildLookupState.FoundWithId || materialLookup.Slot is null)
            {
                continue;
            }

            string? existingComponentId = materialLookup.Slot.Components?
                .Where(component => string.Equals(component.ComponentType, materialComponentType, StringComparison.Ordinal))
                .OrderBy(static component => component.ID, StringComparer.Ordinal)
                .Select(static component => component.ID)
                .FirstOrDefault(static id => !string.IsNullOrWhiteSpace(id));
            CreatedSlot reusableSlot = new(
                new ResoniteSlotLocator(materialLookup.Slot.ID!),
                materialLookup.Slot.Name?.Value ?? materialSlotName);
            if (!string.IsNullOrWhiteSpace(existingComponentId))
            {
                return (reusableSlot, new ResoniteComponentLocator(existingComponentId));
            }

            if (materialLookup.Slot.Components?.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Common material slot '{materialSlotName}' exists but does not contain material component '{materialComponentType}'. "
                    + "Remove the incomplete common material slot before retrying.");
            }

            reusableSlotWithoutComponent ??= reusableSlot;
        }

        return (reusableSlotWithoutComponent, null);
    }

    private readonly record struct PreparedCommonMaterialBatchEntry(
        ResoniteCommonMaterialPlan MaterialPlan,
        ResoniteMaterialBinding Material,
        ResoniteBatchOperations.PendingBatchSlot? PendingMaterialSlot,
        ResoniteBatchOperations.PendingBatchComponent PendingMaterialComponent);
}
