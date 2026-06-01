using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Application.Logging;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteCommonMaterialSetupPreparer
{
    Task PrepareAsync(
        IResoniteLinkClient client,
        ResoniteSceneSetupState setupState,
        CommonMaterialAssetCache materials,
        CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials,
        Action<string>? progressReporter,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteCommonMaterialSetupPreparer(
    IResoniteMaterialPlanning materialPlanning) : IResoniteCommonMaterialSetupPreparer
{
    public async Task PrepareAsync(
        IResoniteLinkClient client,
        ResoniteSceneSetupState setupState,
        CommonMaterialAssetCache materials,
        CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials,
        Action<string>? progressReporter,
        CancellationToken cancellationToken)
    {
        CommonMaterialCatalog<ResoniteCommonMaterialPlan> commonMaterialPlans =
            ResoniteCommonMaterialPlans.CreateCatalogPlans(commonMaterials);
        if (commonMaterialPlans.Count == 0)
        {
            ReportProgress(
                progressReporter,
                PlateauLog.Info("live", "No common material assets are required during scene setup."));
            return;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        int preparedCount = 0;
        ReportProgress(
            progressReporter,
            PlateauLog.Info(
                "live",
                $"Preparing {commonMaterialPlans.Count} common material assets during scene setup before object streaming."));
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
            ReportProgress(
                progressReporter,
                PlateauLog.Info(
                    "live",
                    $"Preparing common material asset {preparedCount + 1}/{commonMaterialPlans.Count}: "
                    + $"family='{familySlotName}', slot='{materialSlotName}'."));
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
                ReportProgress(
                    progressReporter,
                    PlateauLog.Info(
                        "live",
                        $"Reused common material asset {preparedCount}/{commonMaterialPlans.Count}: "
                        + $"family='{familySlotName}', slot='{materialSlotName}', elapsed_s={materialStopwatch.Elapsed.TotalSeconds:F2}."));
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
            ReportProgress(
                progressReporter,
                PlateauLog.Info(
                    "live",
                    $"Planned common material asset {preparedCount}/{commonMaterialPlans.Count}: "
                    + $"family='{familySlotName}', slot='{materialSlotName}', texture_import_elapsed_s={materialStopwatch.Elapsed.TotalSeconds:F2}."));
        }

        if (batchBuilder.Actions.Count > 0)
        {
            BatchResponse response = await client.RunDataModelOperationBatchAsync(batchBuilder.Actions, cancellationToken);
            CanonicalBatchEntityMap entityMap = CanonicalBatchEntityMap.Create(response, batchBuilder.PendingActions);
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

            ReportProgress(
                progressReporter,
                PlateauLog.Info(
                    "live",
                    $"Created {preparedMaterials.Count} common material assets in one setup component batch."));
        }

        foreach (string family in setupState.CommonMaterialFamilies)
        {
            materials.CommonMaterialFamilyWarmupTasks[family] = Task.CompletedTask;
        }

        ReportProgress(
            progressReporter,
            PlateauLog.Info(
                "live",
                $"Prepared {preparedCount} common material assets during scene setup in {stopwatch.Elapsed.TotalSeconds:F2}s."));
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

    private static void ReportProgress(
        Action<string>? progressReporter,
        string message)
    {
        progressReporter?.Invoke(message);
    }

    private readonly record struct PreparedCommonMaterialBatchEntry(
        ResoniteCommonMaterialPlan MaterialPlan,
        ResoniteMaterialBinding Material,
        ResoniteBatchOperations.PendingBatchSlot? PendingMaterialSlot,
        ResoniteBatchOperations.PendingBatchComponent PendingMaterialComponent);
}
