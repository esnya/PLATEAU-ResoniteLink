using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite.Execution;

internal sealed class ResoniteSceneBootstrapInterpreter : IResoniteSceneBootstrapInterpreter
{
    private const string LicenseComponentType = "[FrooxEngine]FrooxEngine.License";
    private const string StaticTextureComponentType = "[FrooxEngine]FrooxEngine.StaticTexture2D";
    private const string SharedAssetsRootName = "PLATEAU Shared Assets";
    private const string SharedCommonMaterialsRootName = "Common Materials";
    private const float DefaultNormalScale = 1.0f;
    private const float DefaultBundledHeightScale = 0.002f;

    private readonly IResoniteSceneSlotLocator sceneSlotLocator;
    private readonly IResoniteSceneAnchorResolver sceneAnchorResolver;
    private readonly IResoniteMaterialPlanning materialPlanning;

    internal ResoniteSceneBootstrapInterpreter(
        IResoniteSceneSlotLocator sceneSlotLocator,
        IResoniteMaterialPlanning materialPlanning,
        IResoniteSceneAnchorResolver sceneAnchorResolver)
    {
        this.sceneSlotLocator = sceneSlotLocator ?? throw new ArgumentNullException(nameof(sceneSlotLocator));
        this.materialPlanning = materialPlanning ?? throw new ArgumentNullException(nameof(materialPlanning));
        this.sceneAnchorResolver = sceneAnchorResolver ?? throw new ArgumentNullException(nameof(sceneAnchorResolver));
    }

    public async Task<ResoniteSceneBootstrapState> BootstrapAsync(
        IResoniteLinkClient setupClient,
        ResoniteSceneBootstrapInfo setupInfo,
        IReadOnlyList<ResoniteMaterialBinding> commonMaterials,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(setupClient);
        ArgumentNullException.ThrowIfNull(setupInfo);
        ArgumentNullException.ThrowIfNull(commonMaterials);

        string completionMeshCode = ResoniteSourceMeshCodeAnchor.ResolveCompletionMeshCode(setupInfo);
        string datasetRootName = $"PLATEAU {setupInfo.Dataset}";
        Slot rootSnapshot = await setupClient.GetSlotAsync(new ResoniteTransportSlotLocator(ResoniteSlotLocator.Root.Value), 4, cancellationToken)
            ?? throw new InvalidOperationException("ResoniteLink did not surface the Root slot during bootstrap.");
        ResoniteSceneSlotSnapshot rootSlotSnapshot = new(rootSnapshot);
        Slot? sharedAssetsSlot = GetReusableChildSlot(rootSlotSnapshot, SharedAssetsRootName, "Root");
        Slot? sharedCommonMaterialsSlot = sharedAssetsSlot is null
            ? null
            : GetReusableChildSlot(new ResoniteSceneSlotSnapshot(sharedAssetsSlot), SharedCommonMaterialsRootName, sharedAssetsSlot.ID!);
        CreatedSlot? existingDatasetRoot = await sceneSlotLocator.TryGetDatasetRootAsync(
            setupClient,
            datasetRootName,
            cancellationToken);

        if (existingDatasetRoot is null)
        {
            return await CreateInitialBootstrapStateAsync(
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
            pendingAssets = ResoniteBatchOperations.CreatePendingSlot("bootstrap_assets_root", "Assets", batchScopeToken);
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
            pendingSharedAssets = ResoniteBatchOperations.CreatePendingSlot("bootstrap_shared_assets_root", SharedAssetsRootName, batchScopeToken);
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
            pendingSharedCommon = ResoniteBatchOperations.CreatePendingSlot("bootstrap_shared_common_materials_root", SharedCommonMaterialsRootName, batchScopeToken);
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
            ?? throw new InvalidOperationException("Bootstrap could not determine the shared Common Materials parent slot.");

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
                $"bootstrap_dataset_license_{license.ComponentKey}",
                LicenseComponentType,
                batchScopeToken);
            _ = batchBuilder.AddComponent(
                pendingLicense.LocalId,
                pendingLicense.MessageId,
                existingDatasetRoot.Value.Locator.Value,
                LicenseComponentType,
                license.Members);
        }

        (Dictionary<string, CreatedMaterialAsset> commonMaterialAssetsByKey, HashSet<string> commonMaterialFamilies, List<PlannedCommonMaterialBatchEntry> plannedCommonMaterials)
            = await PlanCommonMaterialOperationsAsync(
                setupClient,
                commonMaterials,
                sharedCommonMaterialsSlot,
                commonParentId,
                batchScopeToken,
                batchBuilder,
                cancellationToken);

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
                commonMaterialAssetsByKey[plannedMaterial.MaterialKey] = new CreatedMaterialAsset(
                    entityMap.ResolveComponent(plannedMaterial.PendingMaterialComponent).Locator,
                    null);
            }
        }

        if (assetsSlot is null || sharedCommonMaterialsSlot is null)
        {
            throw new InvalidOperationException("Bootstrap did not resolve required shared slots.");
        }

        return new ResoniteSceneBootstrapState(
            existingDatasetRoot.Value,
            new CreatedSlot(new ResoniteSlotLocator(assetsSlot.ID!), assetsSlot.Name?.Value ?? "Assets"),
            new CreatedSlot(new ResoniteSlotLocator(sharedCommonMaterialsSlot.ID!), sharedCommonMaterialsSlot.Name?.Value ?? SharedCommonMaterialsRootName),
            DatasetRootExisted: true,
            sceneAnchor,
            datasetRootSnapshot,
            commonMaterialAssetsByKey,
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

    private async Task<ResoniteSceneBootstrapState> CreateInitialBootstrapStateAsync(
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
        ResoniteBatchOperations.PendingBatchSlot pendingDatasetRootSlot = ResoniteBatchOperations.CreatePendingSlot("bootstrap_dataset_root", datasetRootName, batchScopeToken);
        ResoniteBatchOperations.PendingBatchSlot pendingDatasetAssetsRootSlot = ResoniteBatchOperations.CreatePendingSlot("bootstrap_assets_root", "Assets", batchScopeToken);
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
            pendingSharedAssetsRootSlot = ResoniteBatchOperations.CreatePendingSlot("bootstrap_shared_assets_root", SharedAssetsRootName, batchScopeToken);
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
            pendingSharedCommonMaterialsRootSlot = ResoniteBatchOperations.CreatePendingSlot("bootstrap_shared_common_materials_root", SharedCommonMaterialsRootName, batchScopeToken);
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
                $"bootstrap_dataset_license_{datasetLicense.ComponentKey}",
                LicenseComponentType,
                batchScopeToken);
            _ = batchBuilder.AddComponent(
                pendingLicense.LocalId,
                pendingLicense.MessageId,
                pendingDatasetRootSlot.LocalId.Value,
                LicenseComponentType,
                datasetLicense.Members);
        }

        (Dictionary<string, CreatedMaterialAsset> commonMaterialAssetsByKey, HashSet<string> commonMaterialFamilies, List<PlannedCommonMaterialBatchEntry> plannedCommonMaterials)
            = await PlanCommonMaterialOperationsAsync(
                setupClient,
                commonMaterials,
                commonSlot: existingSharedCommonMaterialsSlot,
                commonParentId: existingSharedCommonMaterialsSlot?.ID
                    ?? pendingSharedCommonMaterialsRootSlot?.LocalId.Value
                    ?? throw new InvalidOperationException("Bootstrap could not determine the shared Common Materials parent slot."),
                batchScopeToken,
                batchBuilder,
                cancellationToken);

        BatchResponse response = await setupClient.RunDataModelOperationBatchAsync(batchBuilder.Actions, cancellationToken);

        CanonicalBatchEntityMap entityMap = CanonicalBatchEntityMap.Create(response);
        CreatedSlot datasetRootSlot = entityMap.ResolveSlot(pendingDatasetRootSlot);
        CreatedSlot datasetAssetsRootSlot = entityMap.ResolveSlot(pendingDatasetAssetsRootSlot);
        CreatedSlot commonAssetsRootSlot = existingSharedCommonMaterialsSlot is null
            ? entityMap.ResolveSlot(pendingSharedCommonMaterialsRootSlot ?? throw new InvalidOperationException("Shared Common Materials slot was not planned."))
            : new CreatedSlot(new ResoniteSlotLocator(existingSharedCommonMaterialsSlot.ID!), existingSharedCommonMaterialsSlot.Name?.Value ?? SharedCommonMaterialsRootName);
        foreach (PlannedCommonMaterialBatchEntry plannedMaterial in plannedCommonMaterials)
        {
            commonMaterialAssetsByKey[plannedMaterial.MaterialKey] = new CreatedMaterialAsset(
                entityMap.ResolveComponent(plannedMaterial.PendingMaterialComponent).Locator,
                null);
        }

        return new ResoniteSceneBootstrapState(
            datasetRootSlot,
            datasetAssetsRootSlot,
            commonAssetsRootSlot,
            DatasetRootExisted: false,
            new SceneAnchor(datasetRootSlot.Locator, completionMeshCode, anchorPosition, ReferenceSourceFileRoot: null),
            DatasetRootSnapshot: null,
            commonMaterialAssetsByKey,
            commonMaterialFamilies);
    }

    private static DatasetLicenseDefinition[] CreateDatasetLicensePlan(ResoniteSceneBootstrapInfo setupInfo)
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

    private async Task<(Dictionary<string, CreatedMaterialAsset> CommonMaterialAssetsByKey, HashSet<string> CommonMaterialFamilies, List<PlannedCommonMaterialBatchEntry> PlannedCommonMaterials)> PlanCommonMaterialOperationsAsync(
        IResoniteLinkClient setupClient,
        IReadOnlyList<ResoniteMaterialBinding> commonMaterials,
        Slot? commonSlot,
        string commonParentId,
        string batchScopeToken,
        ResoniteBatchOperations.BatchActionBuilder batchBuilder,
        CancellationToken cancellationToken)
    {
        Dictionary<string, CreatedMaterialAsset> commonMaterialAssetsByKey = new(StringComparer.Ordinal);
        HashSet<string> commonMaterialFamilies = new(StringComparer.Ordinal);
        List<PlannedCommonMaterialBatchEntry> plannedCommonMaterials = [];
        Dictionary<string, ResoniteMaterialBinding> canonicalMaterialsByKey = CollectCanonicalCommonMaterials(commonMaterials);
        ResoniteSceneSlotSnapshot? commonSlotSnapshot = commonSlot is null ? null : new ResoniteSceneSlotSnapshot(commonSlot);
        string? commonSlotId = commonSlot?.ID;
        Dictionary<string, string> familyParentIds = new(StringComparer.Ordinal);

        foreach (ResoniteMaterialBinding material in canonicalMaterialsByKey.Values.OrderBy(static material => material.MaterialKey, StringComparer.Ordinal))
        {
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
                        commonSlotId ?? throw new InvalidOperationException("Existing shared Common Materials slot did not expose an ID."));
                if (existingFamilySlot is null)
                {
                    ResoniteBatchOperations.PendingBatchSlot pendingFamilySlot = ResoniteBatchOperations.CreatePendingSlot(
                        $"bootstrap_common_material_family_{plannedCommonMaterials.Count}",
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

            string materialSlotName = ResoniteSceneMaterialConventions.CreateMaterialSlotName(material, useCommonMaterialAssets: true);
            IReadOnlyList<string> materialSlotLookupNames = ResoniteSceneMaterialConventions.CreateCommonMaterialSlotLookupNames(material);
            Slot? familySnapshotSlot = commonSlotSnapshot is null
                ? null
                : GetReusableChildSlot(
                    commonSlotSnapshot,
                    familySlotName,
                    commonSlotId ?? throw new InvalidOperationException("Existing shared Common Materials slot did not expose an ID."));
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
                commonMaterialAssetsByKey[material.MaterialKey] = new CreatedMaterialAsset(
                    new ResoniteComponentLocator(existingMaterialComponentId),
                    null);
                continue;
            }

            PlannedDedicatedMaterialAsset plannedMaterial = await this.materialPlanning.PlanCommonMaterialAssetAsync(
                setupClient,
                material,
                cancellationToken);
            int materialIndex = plannedCommonMaterials.Count;
            ResoniteBatchOperations.PendingBatchSlot? pendingMaterialSlot = null;
            string materialContainerId = existingMaterialSlot?.ID ?? string.Empty;
            if (existingMaterialSlot is null)
            {
                pendingMaterialSlot = ResoniteBatchOperations.CreatePendingSlot(
                    $"bootstrap_common_material_slot_{materialIndex}",
                    materialSlotName,
                    batchScopeToken);
                materialContainerId = pendingMaterialSlot.Value.LocalId.Value;
                _ = batchBuilder.AddSlot(
                    pendingMaterialSlot.Value.LocalId,
                    pendingMaterialSlot.Value.MessageId,
                    familyParentId,
                    materialSlotName,
                    null,
                    null);
            }

            ResoniteBatchOperations.PendingBatchComponent pendingMaterialComponent = AddCommonMaterialComponentOperations(
                materialContainerId,
                plannedMaterial,
                batchScopeToken,
                batchBuilder,
                materialIndex);
            plannedCommonMaterials.Add(new PlannedCommonMaterialBatchEntry(material.MaterialKey, family, pendingMaterialComponent));
        }

        return (commonMaterialAssetsByKey, commonMaterialFamilies, plannedCommonMaterials);
    }

    private static Dictionary<string, ResoniteMaterialBinding> CollectCanonicalCommonMaterials(IReadOnlyList<ResoniteMaterialBinding> commonMaterials)
    {
        Dictionary<string, ResoniteMaterialBinding> canonicalMaterialsByKey = new(StringComparer.Ordinal);
        foreach (ResoniteMaterialBinding material in commonMaterials)
        {
            ResoniteMaterialBinding normalizedMaterial = ResoniteSceneMaterialConventions.NormalizeCommonMaterialBinding(material);
            if (normalizedMaterial.AssetScope == ResoniteMaterialAssetScope.Common)
            {
                canonicalMaterialsByKey.TryAdd(normalizedMaterial.MaterialKey, normalizedMaterial);
                continue;
            }

            if (ResoniteSceneMaterialConventions.TryNormalizeSharedMaterialBinding(
                    material,
                    out ResoniteMaterialBinding normalizedSharedMaterial,
                    out _))
            {
                canonicalMaterialsByKey.TryAdd(normalizedSharedMaterial.MaterialKey, normalizedSharedMaterial);
            }
        }

        return canonicalMaterialsByKey;
    }

    private static ResoniteBatchOperations.PendingBatchComponent AddCommonMaterialComponentOperations(
        string materialContainerId,
        PlannedDedicatedMaterialAsset plannedMaterial,
        string batchScopeToken,
        ResoniteBatchOperations.BatchActionBuilder batchBuilder,
        int materialIndex)
    {
        ResoniteMaterialBinding material = plannedMaterial.Material;
        Dictionary<string, Member> materialMembers = ResoniteMaterialComponentPolicy.CreateMembers(material);
        string componentPrefix = $"bootstrap_common_material_component_{materialIndex}";

        Uri? albedoTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(
            plannedMaterial.Textures,
            ResoniteSceneMaterialConventions.TextureMemberRole.Albedo);
        if (albedoTextureUri is not null)
        {
            ResoniteBatchOperations.PendingBatchComponent albedoTexture = ResoniteBatchOperations.CreatePendingComponent(
                $"{componentPrefix}_albedo",
                StaticTextureComponentType,
                batchScopeToken);
            _ = batchBuilder.AddComponent(
                albedoTexture.LocalId,
                albedoTexture.MessageId,
                materialContainerId,
                StaticTextureComponentType,
                ResoniteSceneMaterialConventions.CreateTextureMembers(
                    albedoTextureUri,
                    ResoniteSceneMaterialConventions.TextureMemberRole.Albedo));
            materialMembers["AlbedoTexture"] = new Reference
            {
                TargetID = albedoTexture.LocalId.Value,
            };
        }

        Uri? normalTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(
            plannedMaterial.Textures,
            ResoniteSceneMaterialConventions.TextureMemberRole.Normal);
        if (normalTextureUri is not null)
        {
            ResoniteBatchOperations.PendingBatchComponent normalTexture = ResoniteBatchOperations.CreatePendingComponent(
                $"{componentPrefix}_normal",
                StaticTextureComponentType,
                batchScopeToken);
            _ = batchBuilder.AddComponent(
                normalTexture.LocalId,
                normalTexture.MessageId,
                materialContainerId,
                StaticTextureComponentType,
                ResoniteSceneMaterialConventions.CreateTextureMembers(
                    normalTextureUri,
                    ResoniteSceneMaterialConventions.TextureMemberRole.Normal));
            materialMembers["NormalMap"] = new Reference
            {
                TargetID = normalTexture.LocalId.Value,
            };
            materialMembers["NormalScale"] = new Field_float
            {
                Value = DefaultNormalScale,
            };
        }

        Uri? heightTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(
            plannedMaterial.Textures,
            ResoniteSceneMaterialConventions.TextureMemberRole.Height);
        if (heightTextureUri is not null)
        {
            ResoniteBatchOperations.PendingBatchComponent heightTexture = ResoniteBatchOperations.CreatePendingComponent(
                $"{componentPrefix}_height",
                StaticTextureComponentType,
                batchScopeToken);
            _ = batchBuilder.AddComponent(
                heightTexture.LocalId,
                heightTexture.MessageId,
                materialContainerId,
                StaticTextureComponentType,
                ResoniteSceneMaterialConventions.CreateTextureMembers(
                    heightTextureUri,
                    ResoniteSceneMaterialConventions.TextureMemberRole.Height));
            materialMembers["HeightMap"] = new Reference
            {
                TargetID = heightTexture.LocalId.Value,
            };
            materialMembers["HeightScale"] = new Field_float
            {
                Value = DefaultBundledHeightScale,
            };
        }

        Uri? metallicTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(
            plannedMaterial.Textures,
            ResoniteSceneMaterialConventions.TextureMemberRole.Metallic);
        if (metallicTextureUri is not null)
        {
            ResoniteBatchOperations.PendingBatchComponent metallicTexture = ResoniteBatchOperations.CreatePendingComponent(
                $"{componentPrefix}_metallic",
                StaticTextureComponentType,
                batchScopeToken);
            _ = batchBuilder.AddComponent(
                metallicTexture.LocalId,
                metallicTexture.MessageId,
                materialContainerId,
                StaticTextureComponentType,
                ResoniteSceneMaterialConventions.CreateTextureMembers(
                    metallicTextureUri,
                    ResoniteSceneMaterialConventions.TextureMemberRole.Metallic));
            materialMembers["MetallicMap"] = new Reference
            {
                TargetID = metallicTexture.LocalId.Value,
            };
            materialMembers["OcclusionMap"] = new Reference
            {
                TargetID = metallicTexture.LocalId.Value,
            };
        }

        Uri? emissionTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(
            plannedMaterial.Textures,
            ResoniteSceneMaterialConventions.TextureMemberRole.Emission);
        if (emissionTextureUri is not null)
        {
            ResoniteBatchOperations.PendingBatchComponent emissionTexture = ResoniteBatchOperations.CreatePendingComponent(
                $"{componentPrefix}_emission",
                StaticTextureComponentType,
                batchScopeToken);
            _ = batchBuilder.AddComponent(
                emissionTexture.LocalId,
                emissionTexture.MessageId,
                materialContainerId,
                StaticTextureComponentType,
                ResoniteSceneMaterialConventions.CreateTextureMembers(
                    emissionTextureUri,
                    ResoniteSceneMaterialConventions.TextureMemberRole.Emission));
            materialMembers["EmissiveMap"] = new Reference
            {
                TargetID = emissionTexture.LocalId.Value,
            };
            materialMembers["EmissiveColor"] = ResoniteMaterialComponentPolicy.CreateColorMember(
                new ResoniteColor(1.0, 1.0, 1.0, 1.0));
        }

        ResoniteBatchOperations.PendingBatchComponent pendingMaterialComponent = ResoniteBatchOperations.CreatePendingComponent(
            componentPrefix,
            ResoniteMaterialComponentPolicy.GetComponentType(material),
            batchScopeToken);
        _ = batchBuilder.AddComponent(
            pendingMaterialComponent.LocalId,
            pendingMaterialComponent.MessageId,
            materialContainerId,
            ResoniteMaterialComponentPolicy.GetComponentType(material),
            materialMembers);
        return pendingMaterialComponent;
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
        string MaterialKey,
        string Family,
        ResoniteBatchOperations.PendingBatchComponent PendingMaterialComponent);

    private sealed record DatasetLicenseDefinition(
        string ComponentKey,
        string DeduplicationKey,
        ResoniteLicenseAttributionMetadata License,
        IReadOnlyDictionary<string, Member> Members);
}
