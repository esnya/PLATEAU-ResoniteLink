using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite.Execution;

internal sealed class ResoniteSceneSetupInterpreter : IResoniteSceneSetupInterpreter
{
    private const string LicenseComponentType = "[FrooxEngine]FrooxEngine.License";
    private const string SharedAssetsRootName = "PLATEAU Shared Assets";
    private const string SharedCommonMaterialsRootName = "Common Materials";

    private readonly IResoniteSceneSlotLocator sceneSlotLocator;
    private readonly IResoniteSceneAnchorResolver sceneAnchorResolver;

    internal ResoniteSceneSetupInterpreter(
        IResoniteSceneSlotLocator sceneSlotLocator,
        IResoniteSceneAnchorResolver sceneAnchorResolver)
    {
        this.sceneSlotLocator = sceneSlotLocator ?? throw new ArgumentNullException(nameof(sceneSlotLocator));
        this.sceneAnchorResolver = sceneAnchorResolver ?? throw new ArgumentNullException(nameof(sceneAnchorResolver));
    }

    public async Task<ResoniteSceneSetupState> SetupAsync(
        IResoniteLinkClient setupClient,
        ResoniteSceneSetupInfo setupInfo,
        IReadOnlyList<ResoniteMaterialBinding> commonMaterials,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(setupClient);
        ArgumentNullException.ThrowIfNull(setupInfo);
        ArgumentNullException.ThrowIfNull(commonMaterials);

        string completionMeshCode = ResoniteSourceMeshCodeAnchor.ResolveCompletionMeshCode(setupInfo);
        string datasetRootName = $"PLATEAU {setupInfo.Dataset}";
        Slot rootSnapshot = await setupClient.GetSlotAsync(new ResoniteTransportSlotLocator(ResoniteSlotLocator.Root.Value), 1, cancellationToken)
            ?? throw new InvalidOperationException("ResoniteLink did not surface the Root slot during setup.");
        ResoniteSceneSlotSnapshot rootSlotSnapshot = new(rootSnapshot);
        Slot? sharedAssetsSlot = GetReusableChildSlot(rootSlotSnapshot, SharedAssetsRootName, "Root");
        Slot? sharedCommonMaterialsSlot = await TryGetExistingCommonMaterialsSlotAsync(
            setupClient,
            sharedAssetsSlot,
            cancellationToken);
        CreatedSlot? existingDatasetRoot = await sceneSlotLocator.TryGetDatasetRootAsync(
            setupClient,
            datasetRootName,
            cancellationToken);

        if (existingDatasetRoot is null)
        {
            return await CreateInitialSetupStateAsync(
                setupClient,
                setupInfo.Dataset,
                completionMeshCode,
                CreateDatasetLicensePlan(setupInfo),
                commonMaterials,
                sharedAssetsSlot,
                sharedCommonMaterialsSlot,
                cancellationToken);
        }

        Slot datasetRootSnapshot = await setupClient.GetSlotAsync(
                new ResoniteTransportSlotLocator(existingDatasetRoot.Value.Locator.Value),
                3,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"ResoniteLink did not surface dataset root '{existingDatasetRoot.Value.Locator.Value}' after it was discovered.");

        ResoniteSceneSlotSnapshot snapshot = new(datasetRootSnapshot);
        ResoniteSceneChildLookupResult assetsLookup = snapshot.GetUniqueChildLookupResult("Assets", existingDatasetRoot.Value.Locator.Value);
        Slot? assetsSlot = assetsLookup.State == ResoniteSceneChildLookupState.FoundWithId
            ? assetsLookup.Slot
            : null;
        DatasetLicenseDefinition[] datasetLicenses = CreateDatasetLicensePlan(setupInfo);
        HashSet<string> matchedExistingLicenseKeys = MatchExistingLicenseKeys(datasetRootSnapshot, datasetLicenses);

        ResoniteBatchOperations.BatchActionBuilder batchBuilder = new();
        ResoniteBatchOperations.PendingBatchSlot? pendingAssets = null;
        ResoniteBatchOperations.PendingBatchSlot? pendingSharedAssets = null;
        ResoniteBatchOperations.PendingBatchSlot? pendingSharedCommon = null;
        string batchScopeToken = ResoniteBatchOperations.CreateBatchScopeToken();

        string assetsParentId = existingDatasetRoot.Value.Locator.Value;
        if (assetsSlot is null)
        {
            pendingAssets = ResoniteBatchOperations.CreatePendingSlot("setup_assets_root", "Assets", batchScopeToken);
            _ = batchBuilder.AddSlot(
                pendingAssets.Value.LocalId,
                pendingAssets.Value.MessageId,
                existingDatasetRoot.Value.Locator.Value,
                "Assets",
                null,
                null);
            assetsParentId = pendingAssets.Value.LocalId.Value;
        }
        else
        {
            assetsParentId = assetsSlot.ID ?? throw new InvalidOperationException("Existing Assets slot did not expose an ID.");
        }

        string sharedAssetsParentId = sharedAssetsSlot?.ID ?? "Root";
        if (sharedAssetsSlot is null)
        {
            pendingSharedAssets = ResoniteBatchOperations.CreatePendingSlot("setup_shared_assets_root", SharedAssetsRootName, batchScopeToken);
            _ = batchBuilder.AddSlot(
                pendingSharedAssets.Value.LocalId,
                pendingSharedAssets.Value.MessageId,
                "Root",
                SharedAssetsRootName,
                null,
                null);
            sharedAssetsParentId = pendingSharedAssets.Value.LocalId.Value;
        }

        if (sharedCommonMaterialsSlot is null)
        {
            pendingSharedCommon = ResoniteBatchOperations.CreatePendingSlot("setup_shared_common_materials_root", SharedCommonMaterialsRootName, batchScopeToken);
            _ = batchBuilder.AddSlot(
                pendingSharedCommon.Value.LocalId,
                pendingSharedCommon.Value.MessageId,
                sharedAssetsParentId,
                SharedCommonMaterialsRootName,
                null,
                null);
        }

        string commonParentId = sharedCommonMaterialsSlot?.ID
            ?? pendingSharedCommon?.LocalId.Value
            ?? throw new InvalidOperationException("Setup could not determine the Common Materials parent slot.");

        SceneAnchor sceneAnchor = await sceneAnchorResolver.ResolveAsync(
            setupClient,
            existingDatasetRoot.Value.Locator,
            completionMeshCode,
            cancellationToken);

        foreach (DatasetLicenseDefinition license in datasetLicenses)
        {
            if (matchedExistingLicenseKeys.Contains(license.DeduplicationKey))
            {
                continue;
            }

            ResoniteBatchOperations.PendingBatchComponent pendingLicense = ResoniteBatchOperations.CreatePendingComponent(
                $"setup_dataset_license_{license.ComponentKey}",
                LicenseComponentType,
                batchScopeToken);
            _ = batchBuilder.AddComponent(
                pendingLicense.LocalId,
                pendingLicense.MessageId,
                existingDatasetRoot.Value.Locator.Value,
                LicenseComponentType,
                license.Members);
        }

        (ResoniteCommonMaterialAssetSet commonMaterialAssets, HashSet<string> commonMaterialFamilies, List<PlannedCommonMaterialBatchEntry> plannedCommonMaterials)
            = PlanCommonMaterialOperations(
                commonMaterials,
                sharedCommonMaterialsSlot,
                commonParentId,
                batchScopeToken,
                batchBuilder);

        if (batchBuilder.Actions.Count > 0)
        {
            BatchResponse response = await setupClient.RunDataModelOperationBatchAsync(batchBuilder.Actions, cancellationToken);
            CanonicalBatchEntityMap entityMap = CanonicalBatchEntityMap.Create(response);
            if (pendingAssets is not null)
            {
                assetsSlot = CreateSlot(entityMap.ResolveSlot(pendingAssets.Value));
            }

            if (pendingSharedCommon is not null)
            {
                sharedCommonMaterialsSlot = CreateSlot(entityMap.ResolveSlot(pendingSharedCommon.Value));
            }

            foreach (PlannedCommonMaterialBatchEntry plannedMaterial in plannedCommonMaterials)
            {
                commonMaterialAssets.Set(new ResoniteCommonMaterialAsset(
                    plannedMaterial.MaterialPlan.Material,
                    new CreatedMaterialAsset(
                        entityMap.ResolveComponent(plannedMaterial.PendingMaterialComponent).Locator,
                        null)));
            }
        }

        if (assetsSlot is null || sharedCommonMaterialsSlot is null)
        {
            throw new InvalidOperationException("Setup did not resolve required shared slots.");
        }

        return new ResoniteSceneSetupState(
            existingDatasetRoot.Value,
            new CreatedSlot(new ResoniteSlotLocator(assetsSlot.ID!), assetsSlot.Name?.Value ?? "Assets"),
            new CreatedSlot(new ResoniteSlotLocator(sharedCommonMaterialsSlot.ID!), sharedCommonMaterialsSlot.Name?.Value ?? SharedCommonMaterialsRootName),
            DatasetRootExisted: true,
            sceneAnchor,
            datasetRootSnapshot,
            commonMaterialAssets,
            commonMaterialFamilies);
    }

    private static Slot? GetReusableChildSlot(
        ResoniteSceneSlotSnapshot snapshot,
        string slotName,
        string parentId)
    {
        ResoniteSceneChildLookupResult lookup = snapshot.GetUniqueChildLookupResult(slotName, parentId);
        return lookup.State == ResoniteSceneChildLookupState.FoundWithId
            ? lookup.Slot
            : null;
    }

    private static async Task<Slot?> TryGetExistingCommonMaterialsSlotAsync(
        IResoniteLinkClient setupClient,
        Slot? sharedAssetsSlot,
        CancellationToken cancellationToken)
    {
        if (sharedAssetsSlot?.ID is null)
        {
            return null;
        }

        Slot? sharedAssetsSnapshot = await setupClient.GetSlotAsync(
            new ResoniteTransportSlotLocator(sharedAssetsSlot.ID),
            1,
            cancellationToken);
        Slot? commonMaterialsSlot = sharedAssetsSnapshot is null
            ? null
            : GetReusableChildSlot(
                new ResoniteSceneSlotSnapshot(sharedAssetsSnapshot),
                SharedCommonMaterialsRootName,
                sharedAssetsSlot.ID);
        if (commonMaterialsSlot?.ID is null)
        {
            return commonMaterialsSlot;
        }

        return await setupClient.GetSlotAsync(
            new ResoniteTransportSlotLocator(commonMaterialsSlot.ID),
            2,
            cancellationToken);
    }

    private static async Task<ResoniteSceneSetupState> CreateInitialSetupStateAsync(
        IResoniteLinkClient setupClient,
        string datasetName,
        string completionMeshCode,
        IReadOnlyList<DatasetLicenseDefinition> datasetLicenses,
        IReadOnlyList<ResoniteMaterialBinding> commonMaterials,
        Slot? existingSharedAssetsSlot,
        Slot? existingSharedCommonMaterialsSlot,
        CancellationToken cancellationToken)
    {
        string datasetRootName = $"PLATEAU {datasetName}";
        ResoniteFloat3 anchorPosition = new(0.0, 0.0, 0.0);
        string batchScopeToken = ResoniteBatchOperations.CreateBatchScopeToken();
        ResoniteBatchOperations.PendingBatchSlot pendingDatasetRootSlot = ResoniteBatchOperations.CreatePendingSlot("setup_dataset_root", datasetRootName, batchScopeToken);
        ResoniteBatchOperations.PendingBatchSlot pendingDatasetAssetsRootSlot = ResoniteBatchOperations.CreatePendingSlot("setup_assets_root", "Assets", batchScopeToken);
        ResoniteBatchOperations.BatchActionBuilder batchBuilder = new();
        _ = batchBuilder.AddSlot(
            pendingDatasetRootSlot.LocalId,
            pendingDatasetRootSlot.MessageId,
            "Root",
            datasetRootName,
            null,
            null);
        _ = batchBuilder.AddSlot(
            pendingDatasetAssetsRootSlot.LocalId,
            pendingDatasetAssetsRootSlot.MessageId,
            pendingDatasetRootSlot.LocalId.Value,
            "Assets",
            null,
            null);
        ResoniteBatchOperations.PendingBatchSlot? pendingSharedAssetsRootSlot = null;
        ResoniteBatchOperations.PendingBatchSlot? pendingSharedCommonMaterialsRootSlot = null;
        string sharedAssetsParentId = existingSharedAssetsSlot?.ID ?? "Root";
        if (existingSharedAssetsSlot is null)
        {
            pendingSharedAssetsRootSlot = ResoniteBatchOperations.CreatePendingSlot("setup_shared_assets_root", SharedAssetsRootName, batchScopeToken);
            _ = batchBuilder.AddSlot(
                pendingSharedAssetsRootSlot.Value.LocalId,
                pendingSharedAssetsRootSlot.Value.MessageId,
                "Root",
                SharedAssetsRootName,
                null,
                null);
            sharedAssetsParentId = pendingSharedAssetsRootSlot.Value.LocalId.Value;
        }

        if (existingSharedCommonMaterialsSlot is null)
        {
            pendingSharedCommonMaterialsRootSlot = ResoniteBatchOperations.CreatePendingSlot("setup_shared_common_materials_root", SharedCommonMaterialsRootName, batchScopeToken);
            _ = batchBuilder.AddSlot(
                pendingSharedCommonMaterialsRootSlot.Value.LocalId,
                pendingSharedCommonMaterialsRootSlot.Value.MessageId,
                sharedAssetsParentId,
                SharedCommonMaterialsRootName,
                null,
                null);
        }

        foreach (DatasetLicenseDefinition datasetLicense in datasetLicenses)
        {
            ResoniteBatchOperations.PendingBatchComponent pendingLicense = ResoniteBatchOperations.CreatePendingComponent(
                $"setup_dataset_license_{datasetLicense.ComponentKey}",
                LicenseComponentType,
                batchScopeToken);
            _ = batchBuilder.AddComponent(
                pendingLicense.LocalId,
                pendingLicense.MessageId,
                pendingDatasetRootSlot.LocalId.Value,
                LicenseComponentType,
                datasetLicense.Members);
        }

        (ResoniteCommonMaterialAssetSet commonMaterialAssets, HashSet<string> commonMaterialFamilies, List<PlannedCommonMaterialBatchEntry> plannedCommonMaterials)
            = PlanCommonMaterialOperations(
                commonMaterials,
                commonSlot: existingSharedCommonMaterialsSlot,
                commonParentId: existingSharedCommonMaterialsSlot?.ID
                    ?? pendingSharedCommonMaterialsRootSlot?.LocalId.Value
                    ?? throw new InvalidOperationException("Setup could not determine the Common Materials parent slot."),
                batchScopeToken,
                batchBuilder);

        BatchResponse response = await setupClient.RunDataModelOperationBatchAsync(batchBuilder.Actions, cancellationToken);

        CanonicalBatchEntityMap entityMap = CanonicalBatchEntityMap.Create(response);
        CreatedSlot datasetRootSlot = entityMap.ResolveSlot(pendingDatasetRootSlot);
        CreatedSlot datasetAssetsRootSlot = entityMap.ResolveSlot(pendingDatasetAssetsRootSlot);
        CreatedSlot commonAssetsRootSlot = existingSharedCommonMaterialsSlot is null
            ? entityMap.ResolveSlot(pendingSharedCommonMaterialsRootSlot ?? throw new InvalidOperationException("Common Materials slot was not planned."))
            : new CreatedSlot(new ResoniteSlotLocator(existingSharedCommonMaterialsSlot.ID!), existingSharedCommonMaterialsSlot.Name?.Value ?? SharedCommonMaterialsRootName);
        foreach (PlannedCommonMaterialBatchEntry plannedMaterial in plannedCommonMaterials)
        {
            commonMaterialAssets.Set(new ResoniteCommonMaterialAsset(
                plannedMaterial.MaterialPlan.Material,
                new CreatedMaterialAsset(
                    entityMap.ResolveComponent(plannedMaterial.PendingMaterialComponent).Locator,
                    null)));
        }

        return new ResoniteSceneSetupState(
            datasetRootSlot,
            datasetAssetsRootSlot,
            commonAssetsRootSlot,
            DatasetRootExisted: false,
            new SceneAnchor(datasetRootSlot.Locator, completionMeshCode, anchorPosition, ReferenceSourceFileRoot: null),
            DatasetRootSnapshot: null,
            commonMaterialAssets,
            commonMaterialFamilies);
    }

    private static DatasetLicenseDefinition[] CreateDatasetLicensePlan(ResoniteSceneSetupInfo setupInfo)
    {
        ArgumentNullException.ThrowIfNull(setupInfo);

        return new[] { setupInfo.DatasetLicense }
            .Distinct()
            .Select((license, index) => new DatasetLicenseDefinition(
                ComponentKey: $"license_{index}",
                DeduplicationKey: CreateLicenseDeduplicationKey(license),
                License: license,
                Members: CreateDatasetLicenseMembers(license)))
            .ToArray();
    }

    private static HashSet<string> MatchExistingLicenseKeys(
        Slot datasetRootSnapshot,
        IReadOnlyList<DatasetLicenseDefinition> datasetLicenses)
    {
        HashSet<string> matchedKeys = [];
        if (datasetRootSnapshot.Components is null || datasetRootSnapshot.Components.Count == 0)
        {
            return matchedKeys;
        }

        foreach (DatasetLicenseDefinition plannedLicense in datasetLicenses)
        {
            bool exists = datasetRootSnapshot.Components
                .Where(static component => string.Equals(component.ComponentType, LicenseComponentType, StringComparison.Ordinal))
                .OrderBy(static component => component.ID, StringComparer.Ordinal)
                .Any(component => LicenseMembersMatch(component, plannedLicense.Members));
            if (exists)
            {
                matchedKeys.Add(plannedLicense.DeduplicationKey);
            }
        }

        return matchedKeys;
    }

    private static string CreateLicenseDeduplicationKey(ResoniteLicenseAttributionMetadata license)
    {
        return string.Join(
            "\n",
            license.RequireCredit.ToString(),
            license.CreditText,
            license.LicenseName,
            license.LicenseUrl);
    }

    private static bool LicenseMembersMatch(Component component, IReadOnlyDictionary<string, Member> members)
    {
        if (!component.Members.TryGetValue("RequireCredit", out Member? requireCreditMember)
            || requireCreditMember is not Field_bool existingRequireCredit
            || members["RequireCredit"] is not Field_bool expectedRequireCredit
            || existingRequireCredit.Value != expectedRequireCredit.Value)
        {
            return false;
        }

        if (!component.Members.TryGetValue("CreditString", out Member? creditStringMember)
            || creditStringMember is not Field_string existingCreditString
            || members["CreditString"] is not Field_string expectedCreditString)
        {
            return false;
        }

        return string.Equals(existingCreditString.Value, expectedCreditString.Value, StringComparison.Ordinal);
    }

    private static (ResoniteCommonMaterialAssetSet CommonMaterialAssets, HashSet<string> CommonMaterialFamilies, List<PlannedCommonMaterialBatchEntry> PlannedCommonMaterials) PlanCommonMaterialOperations(
        IReadOnlyList<ResoniteMaterialBinding> commonMaterials,
        Slot? commonSlot,
        string commonParentId,
        string batchScopeToken,
        ResoniteBatchOperations.BatchActionBuilder batchBuilder)
    {
        ResoniteCommonMaterialAssetSet commonMaterialAssets = new();
        HashSet<string> commonMaterialFamilies = new(StringComparer.Ordinal);
        List<PlannedCommonMaterialBatchEntry> plannedCommonMaterials = [];
        IReadOnlyList<ResoniteCommonMaterialPlan> canonicalMaterials =
            ResoniteCommonMaterialPlans.CreateCatalogPlans(commonMaterials);
        ResoniteSceneSlotSnapshot? commonSlotSnapshot = commonSlot is null ? null : new ResoniteSceneSlotSnapshot(commonSlot);
        string? commonSlotId = commonSlot?.ID;
        Dictionary<string, string> familyParentIds = new(StringComparer.Ordinal);
        int materialSlotIndex = 0;

        foreach (ResoniteCommonMaterialPlan materialPlan in canonicalMaterials)
        {
            ResoniteMaterialBinding material = materialPlan.Material;
            string family = ResoniteSceneMaterialConventions.GetCommonMaterialFamilySlotName(material);
            commonMaterialFamilies.Add(family);
            string familySlotName = family;
            if (!familyParentIds.TryGetValue(family, out string? familyParentId))
            {
                Slot? existingFamilySlot = commonSlotSnapshot is null
                    ? null
                    : GetReusableChildSlot(
                        commonSlotSnapshot,
                        familySlotName,
                        commonSlotId ?? throw new InvalidOperationException("Existing Common Materials slot did not expose an ID."));
                if (existingFamilySlot is null)
                {
                    ResoniteBatchOperations.PendingBatchSlot pendingFamilySlot = ResoniteBatchOperations.CreatePendingSlot(
                        $"setup_common_material_family_{familyParentIds.Count}",
                        familySlotName,
                        batchScopeToken);
                    _ = batchBuilder.AddSlot(
                        pendingFamilySlot.LocalId,
                        pendingFamilySlot.MessageId,
                        commonParentId,
                        familySlotName,
                        null,
                        null);
                    familyParentId = pendingFamilySlot.LocalId.Value;
                }
                else
                {
                    familyParentId = existingFamilySlot.ID ?? throw new InvalidOperationException("Existing common material family slot did not expose an ID.");
                }

                familyParentIds[family] = familyParentId;
            }

            string materialSlotName = materialPlan.SlotName;
            IReadOnlyList<string> materialSlotLookupNames = ResoniteSceneMaterialConventions.CreateCommonMaterialSlotLookupNames(material);
            Slot? familySnapshotSlot = commonSlotSnapshot is null
                ? null
                : GetReusableChildSlot(
                    commonSlotSnapshot,
                    familySlotName,
                    commonSlotId ?? throw new InvalidOperationException("Existing Common Materials slot did not expose an ID."));
            ResoniteSceneSlotSnapshot? familySlotSnapshot = familySnapshotSlot is null ? null : new ResoniteSceneSlotSnapshot(familySnapshotSlot);
            string? familySnapshotSlotId = familySnapshotSlot?.ID;
            Slot? existingMaterialSlot = null;
            Slot? reusableEmptyMaterialSlot = null;
            if (familySlotSnapshot is not null)
            {
                string requiredFamilySlotId = familySnapshotSlotId
                    ?? throw new InvalidOperationException("Existing common material family slot did not expose an ID.");
                foreach (string lookupSlotName in materialSlotLookupNames)
                {
                    Slot? candidateMaterialSlot = GetReusableChildSlot(familySlotSnapshot, lookupSlotName, requiredFamilySlotId);
                    if (candidateMaterialSlot is null)
                    {
                        continue;
                    }

                    reusableEmptyMaterialSlot ??= candidateMaterialSlot;
                    if (candidateMaterialSlot.Components?.Any(component =>
                            string.Equals(component.ComponentType, ResoniteMaterialComponentPolicy.GetComponentType(material), StringComparison.Ordinal)) == true)
                    {
                        existingMaterialSlot = candidateMaterialSlot;
                        break;
                    }
                }
            }
            existingMaterialSlot ??= reusableEmptyMaterialSlot;
            string materialComponentType = ResoniteMaterialComponentPolicy.GetComponentType(material);
            string? existingMaterialComponentId = existingMaterialSlot?.Components?
                .Where(component => string.Equals(component.ComponentType, materialComponentType, StringComparison.Ordinal))
                .OrderBy(static component => component.ID, StringComparer.Ordinal)
                .Select(static component => component.ID)
                .FirstOrDefault(static id => !string.IsNullOrWhiteSpace(id));
            if (!string.IsNullOrWhiteSpace(existingMaterialComponentId))
            {
                commonMaterialAssets.Set(new ResoniteCommonMaterialAsset(
                    material,
                    new CreatedMaterialAsset(
                        new ResoniteComponentLocator(existingMaterialComponentId),
                        null)));
                continue;
            }

            if (existingMaterialSlot?.Components?.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Common material slot '{materialSlotName}' exists but does not contain material component '{materialComponentType}'. "
                    + "Remove the incomplete common material slot before retrying.");
            }

            if (!CanCreateCommonMaterialDuringSetup(material))
            {
                continue;
            }

            int materialIndex = plannedCommonMaterials.Count;
            ResoniteBatchOperations.PendingBatchSlot? pendingMaterialSlot = null;
            string materialContainerId = existingMaterialSlot?.ID ?? string.Empty;
            if (existingMaterialSlot is null)
            {
                pendingMaterialSlot = ResoniteBatchOperations.CreatePendingSlot(
                    $"setup_common_material_slot_{materialSlotIndex}",
                    materialSlotName,
                    batchScopeToken);
                materialSlotIndex++;
                materialContainerId = pendingMaterialSlot.Value.LocalId.Value;
                _ = batchBuilder.AddSlot(
                    pendingMaterialSlot.Value.LocalId,
                    pendingMaterialSlot.Value.MessageId,
                    familyParentId,
                    materialSlotName,
                    null,
                    null);
            }

            ResoniteBatchOperations.PendingBatchComponent pendingMaterialComponent = ResoniteBatchOperations.CreatePendingComponent(
                $"setup_common_material_component_{materialIndex}",
                materialComponentType,
                batchScopeToken);
            _ = batchBuilder.AddComponent(
                pendingMaterialComponent.LocalId,
                pendingMaterialComponent.MessageId,
                materialContainerId,
                materialComponentType,
                ResoniteMaterialComponentPolicy.CreateMembers(material));
            plannedCommonMaterials.Add(new PlannedCommonMaterialBatchEntry(materialPlan, family, pendingMaterialComponent));
        }

        return (commonMaterialAssets, commonMaterialFamilies, plannedCommonMaterials);
    }

    private static bool CanCreateCommonMaterialDuringSetup(ResoniteMaterialBinding material)
    {
        return string.IsNullOrWhiteSpace(material.Family)
            && material.TexturePayload is null
            && material.TerrainOverlay is null;
    }

    private static Dictionary<string, Member> CreateDatasetLicenseMembers(
        ResoniteLicenseAttributionMetadata license)
    {
        return new Dictionary<string, Member>(StringComparer.Ordinal)
        {
            ["RequireCredit"] = new Field_bool
            {
                Value = license.RequireCredit,
            },
            ["CreditString"] = new Field_string
            {
                Value = $"{license.CreditText} License: {license.LicenseName} ({license.LicenseUrl})",
            },
        };
    }

    private static Slot CreateSlot(CreatedSlot createdSlot, ResoniteFloat3? position = null)
    {
        return new Slot
        {
            ID = createdSlot.Locator.Value,
            Name = new Field_string
            {
                Value = createdSlot.SlotName,
            },
            Position = position is null ? null : ResoniteBatchOperations.CreateFloat3(position),
        };
    }

    private readonly record struct PlannedCommonMaterialBatchEntry(
        ResoniteCommonMaterialPlan MaterialPlan,
        string Family,
        ResoniteBatchOperations.PendingBatchComponent PendingMaterialComponent);

    private sealed record DatasetLicenseDefinition(
        string ComponentKey,
        string DeduplicationKey,
        ResoniteLicenseAttributionMetadata License,
        IReadOnlyDictionary<string, Member> Members);
}
