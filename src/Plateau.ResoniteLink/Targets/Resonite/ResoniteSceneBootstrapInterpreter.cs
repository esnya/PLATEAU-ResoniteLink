using System.Security.Cryptography;

using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Targets.Resonite.Execution;

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
        SceneBootstrapInfo setupInfo,
        IReadOnlyList<ResoniteMaterialBinding> commonMaterials,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(setupClient);
        ArgumentNullException.ThrowIfNull(setupInfo);
        ArgumentNullException.ThrowIfNull(commonMaterials);

        string completionMeshCode = ResoniteSourceMeshCodeAnchor.ResolveCompletionMeshCode(setupInfo);
        string datasetRootName = $"PLATEAU {setupInfo.Dataset}";
        Slot rootSnapshot = await setupClient.GetSlotAsync("Root", 4, cancellationToken)
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

        Slot datasetRootSnapshot = await setupClient.GetSlotAsync(existingDatasetRoot.Value.SlotId, 3, cancellationToken)
            ?? throw new InvalidOperationException(
                $"ResoniteLink did not surface dataset root '{existingDatasetRoot.Value.SlotId}' after it was discovered.");

        ResoniteSceneSlotSnapshot snapshot = new(datasetRootSnapshot);
        ResoniteSceneChildLookupResult assetsLookup = snapshot.GetUniqueChildLookupResult("Assets", existingDatasetRoot.Value.SlotId);
        Slot? assetsSlot = assetsLookup.State == ResoniteSceneChildLookupState.FoundWithId
            ? assetsLookup.Slot
            : null;
        DatasetLicenseDefinition[] datasetLicenses = CreateDatasetLicensePlan(setupInfo);
        HashSet<string> matchedExistingLicenseKeys = MatchExistingLicenseKeys(datasetRootSnapshot, datasetLicenses);

        List<DataModelOperation> operations = [];
        ResoniteBatchOperations.PendingBatchSlot? pendingAssets = null;
        ResoniteBatchOperations.PendingBatchSlot? pendingSharedAssets = null;
        ResoniteBatchOperations.PendingBatchSlot? pendingSharedCommon = null;
        string batchScopeToken = CreateBatchScopeToken();

        string assetsParentId = existingDatasetRoot.Value.SlotId;
        if (assetsSlot is null)
        {
            pendingAssets = CreatePendingBatchSlot("bootstrap_assets_root", "Assets", batchScopeToken);
            operations.Add(ResoniteBatchOperations.CreateAddSlotOperation(existingDatasetRoot.Value.SlotId, "Assets", null, null, pendingAssets.Value));
            assetsParentId = pendingAssets.Value.LocalId;
        }
        else
        {
            assetsParentId = assetsSlot.ID ?? throw new InvalidOperationException("Existing Assets slot did not expose an ID.");
        }

        string sharedAssetsParentId = sharedAssetsSlot?.ID ?? "Root";
        if (sharedAssetsSlot is null)
        {
            pendingSharedAssets = CreatePendingBatchSlot("bootstrap_shared_assets_root", SharedAssetsRootName, batchScopeToken);
            operations.Add(ResoniteBatchOperations.CreateAddSlotOperation("Root", SharedAssetsRootName, null, null, pendingSharedAssets.Value));
            sharedAssetsParentId = pendingSharedAssets.Value.LocalId;
        }

        if (sharedCommonMaterialsSlot is null)
        {
            pendingSharedCommon = CreatePendingBatchSlot("bootstrap_shared_common_materials_root", SharedCommonMaterialsRootName, batchScopeToken);
            operations.Add(ResoniteBatchOperations.CreateAddSlotOperation(sharedAssetsParentId, SharedCommonMaterialsRootName, null, null, pendingSharedCommon.Value));
        }

        string commonParentId = sharedCommonMaterialsSlot?.ID
            ?? pendingSharedCommon?.LocalId
            ?? throw new InvalidOperationException("Bootstrap could not determine the shared Common Materials parent slot.");

        SceneAnchor sceneAnchor = await sceneAnchorResolver.ResolveAsync(
            setupClient,
            existingDatasetRoot.Value.SlotId,
            completionMeshCode,
            datasetRootExisted: true,
            cancellationToken);

        foreach (DatasetLicenseDefinition license in datasetLicenses)
        {
            if (matchedExistingLicenseKeys.Contains(license.DeduplicationKey))
            {
                continue;
            }

            ResoniteBatchOperations.PendingBatchComponent pendingLicense = CreatePendingBatchComponent(
                $"bootstrap_dataset_license_{license.ComponentKey}",
                LicenseComponentType,
                batchScopeToken);
            operations.Add(ResoniteBatchOperations.CreateAddComponentOperation(
                existingDatasetRoot.Value.SlotId,
                LicenseComponentType,
                license.Members,
                pendingLicense));
        }

        (Dictionary<string, CreatedMaterialAsset> commonMaterialAssetsByKey, HashSet<string> commonMaterialFamilies, List<PlannedCommonMaterialBatchEntry> plannedCommonMaterials)
            = await PlanCommonMaterialOperationsAsync(
                setupClient,
                commonMaterials,
                sharedCommonMaterialsSlot,
                commonParentId,
                batchScopeToken,
                operations,
                cancellationToken);

        if (operations.Count > 0)
        {
            BatchResponse response = await setupClient.RunDataModelOperationBatchAsync(operations, cancellationToken);
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
                    entityMap.ResolveComponent(plannedMaterial.PendingMaterialComponent).ComponentId,
                    null);
            }
        }

        if (assetsSlot is null || sharedCommonMaterialsSlot is null)
        {
            throw new InvalidOperationException("Bootstrap did not resolve required shared slots.");
        }

        return new ResoniteSceneBootstrapState(
            existingDatasetRoot.Value,
            new CreatedSlot(assetsSlot.ID!, assetsSlot.Name?.Value ?? "Assets"),
            new CreatedSlot(sharedCommonMaterialsSlot.ID!, sharedCommonMaterialsSlot.Name?.Value ?? SharedCommonMaterialsRootName),
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
        string batchScopeToken = CreateBatchScopeToken();
        ResoniteBatchOperations.PendingBatchSlot pendingDatasetRootSlot = CreatePendingBatchSlot("bootstrap_dataset_root", datasetRootName, batchScopeToken);
        ResoniteBatchOperations.PendingBatchSlot pendingDatasetAssetsRootSlot = CreatePendingBatchSlot("bootstrap_assets_root", "Assets", batchScopeToken);
        List<DataModelOperation> operations =
        [
            ResoniteBatchOperations.CreateAddSlotOperation("Root", datasetRootName, null, null, pendingDatasetRootSlot),
            ResoniteBatchOperations.CreateAddSlotOperation(pendingDatasetRootSlot.LocalId, "Assets", null, null, pendingDatasetAssetsRootSlot),
        ];
        ResoniteBatchOperations.PendingBatchSlot? pendingSharedAssetsRootSlot = null;
        ResoniteBatchOperations.PendingBatchSlot? pendingSharedCommonMaterialsRootSlot = null;
        string sharedAssetsParentId = existingSharedAssetsSlot?.ID ?? "Root";
        if (existingSharedAssetsSlot is null)
        {
            pendingSharedAssetsRootSlot = CreatePendingBatchSlot("bootstrap_shared_assets_root", SharedAssetsRootName, batchScopeToken);
            operations.Add(ResoniteBatchOperations.CreateAddSlotOperation("Root", SharedAssetsRootName, null, null, pendingSharedAssetsRootSlot.Value));
            sharedAssetsParentId = pendingSharedAssetsRootSlot.Value.LocalId;
        }

        if (existingSharedCommonMaterialsSlot is null)
        {
            pendingSharedCommonMaterialsRootSlot = CreatePendingBatchSlot("bootstrap_shared_common_materials_root", SharedCommonMaterialsRootName, batchScopeToken);
            operations.Add(ResoniteBatchOperations.CreateAddSlotOperation(sharedAssetsParentId, SharedCommonMaterialsRootName, null, null, pendingSharedCommonMaterialsRootSlot.Value));
        }

        foreach (DatasetLicenseDefinition datasetLicense in datasetLicenses)
        {
            ResoniteBatchOperations.PendingBatchComponent pendingLicense = CreatePendingBatchComponent(
                $"bootstrap_dataset_license_{datasetLicense.ComponentKey}",
                LicenseComponentType,
                batchScopeToken);
            operations.Add(ResoniteBatchOperations.CreateAddComponentOperation(
                pendingDatasetRootSlot.LocalId,
                LicenseComponentType,
                datasetLicense.Members,
                pendingLicense));
        }

        (Dictionary<string, CreatedMaterialAsset> commonMaterialAssetsByKey, HashSet<string> commonMaterialFamilies, List<PlannedCommonMaterialBatchEntry> plannedCommonMaterials)
            = await PlanCommonMaterialOperationsAsync(
                setupClient,
                commonMaterials,
                commonSlot: existingSharedCommonMaterialsSlot,
                commonParentId: existingSharedCommonMaterialsSlot?.ID ?? pendingSharedCommonMaterialsRootSlot?.LocalId ?? throw new InvalidOperationException("Bootstrap could not determine the shared Common Materials parent slot."),
                batchScopeToken,
                operations,
                cancellationToken);

        BatchResponse response = await setupClient.RunDataModelOperationBatchAsync(operations, cancellationToken);

        CanonicalBatchEntityMap entityMap = CanonicalBatchEntityMap.Create(response);
        CreatedSlot datasetRootSlot = entityMap.ResolveSlot(pendingDatasetRootSlot);
        CreatedSlot datasetAssetsRootSlot = entityMap.ResolveSlot(pendingDatasetAssetsRootSlot);
        CreatedSlot commonAssetsRootSlot = existingSharedCommonMaterialsSlot is null
            ? entityMap.ResolveSlot(pendingSharedCommonMaterialsRootSlot ?? throw new InvalidOperationException("Shared Common Materials slot was not planned."))
            : new CreatedSlot(existingSharedCommonMaterialsSlot.ID!, existingSharedCommonMaterialsSlot.Name?.Value ?? SharedCommonMaterialsRootName);
        foreach (PlannedCommonMaterialBatchEntry plannedMaterial in plannedCommonMaterials)
        {
            commonMaterialAssetsByKey[plannedMaterial.MaterialKey] = new CreatedMaterialAsset(
                entityMap.ResolveComponent(plannedMaterial.PendingMaterialComponent).ComponentId,
                null);
        }

        return new ResoniteSceneBootstrapState(
            datasetRootSlot,
            datasetAssetsRootSlot,
            commonAssetsRootSlot,
            DatasetRootExisted: false,
            new SceneAnchor(datasetRootSlot.SlotId, completionMeshCode, anchorPosition, ReferenceSourceFileRootId: null),
            DatasetRootSnapshot: null,
            commonMaterialAssetsByKey,
            commonMaterialFamilies);
    }

    private static DatasetLicenseDefinition[] CreateDatasetLicensePlan(SceneBootstrapInfo setupInfo)
    {
        ArgumentNullException.ThrowIfNull(setupInfo);

        return setupInfo.AdditionalDatasetLicenses
            .Prepend(setupInfo.DatasetLicense)
            .Distinct()
            .Select(static (license, index) => new DatasetLicenseDefinition(
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

    private static string CreateLicenseDeduplicationKey(LicenseAttributionMetadata license)
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
        List<DataModelOperation> operations,
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
            string family = material.Family ?? BundledDefaultMaterialFamilies.Other;
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
                    ResoniteBatchOperations.PendingBatchSlot pendingFamilySlot = CreatePendingBatchSlot(
                        $"bootstrap_common_material_family_{plannedCommonMaterials.Count}",
                        familySlotName,
                        batchScopeToken);
                    operations.Add(ResoniteBatchOperations.CreateAddSlotOperation(commonParentId, familySlotName, null, null, pendingFamilySlot));
                    familyParentId = pendingFamilySlot.LocalId;
                }
                else
                {
                    familyParentId = existingFamilySlot.ID ?? throw new InvalidOperationException("Existing common material family slot did not expose an ID.");
                }

                familyParentIds[family] = familyParentId;
            }

            string materialSlotName = ResoniteSceneMaterialConventions.CreateMaterialSlotName(material, useCommonMaterialAssets: true);
            Slot? familySnapshotSlot = commonSlotSnapshot is null
                ? null
                : GetReusableChildSlot(
                    commonSlotSnapshot,
                    familySlotName,
                    commonSlotId ?? throw new InvalidOperationException("Existing shared Common Materials slot did not expose an ID."));
            ResoniteSceneSlotSnapshot? familySlotSnapshot = familySnapshotSlot is null ? null : new ResoniteSceneSlotSnapshot(familySnapshotSlot);
            string? familySnapshotSlotId = familySnapshotSlot?.ID;
            Slot? existingMaterialSlot = familySlotSnapshot is null
                ? null
                : GetReusableChildSlot(
                    familySlotSnapshot,
                    materialSlotName,
                    familySnapshotSlotId ?? throw new InvalidOperationException("Existing common material family slot did not expose an ID."));
            string materialComponentType = ResoniteMaterialComponentPolicy.GetComponentType(material);
            string? existingMaterialComponentId = existingMaterialSlot?.Components?
                .Where(component => string.Equals(component.ComponentType, materialComponentType, StringComparison.Ordinal))
                .OrderBy(static component => component.ID, StringComparer.Ordinal)
                .Select(static component => component.ID)
                .FirstOrDefault(static id => !string.IsNullOrWhiteSpace(id));
            if (!string.IsNullOrWhiteSpace(existingMaterialComponentId))
            {
                commonMaterialAssetsByKey[material.MaterialKey] = new CreatedMaterialAsset(existingMaterialComponentId, null);
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
                pendingMaterialSlot = CreatePendingBatchSlot(
                    $"bootstrap_common_material_slot_{materialIndex}",
                    materialSlotName,
                    batchScopeToken);
                materialContainerId = pendingMaterialSlot.Value.LocalId;
                operations.Add(ResoniteBatchOperations.CreateAddSlotOperation(familyParentId, materialSlotName, null, null, pendingMaterialSlot.Value));
            }

            ResoniteBatchOperations.PendingBatchComponent pendingMaterialComponent = AddCommonMaterialComponentOperations(
                materialContainerId,
                plannedMaterial,
                batchScopeToken,
                operations,
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
            if (normalizedMaterial.AssetScope != ResoniteMaterialAssetScope.Common)
            {
                continue;
            }

            canonicalMaterialsByKey.TryAdd(normalizedMaterial.MaterialKey, normalizedMaterial);
        }

        return canonicalMaterialsByKey;
    }

    private static ResoniteBatchOperations.PendingBatchComponent AddCommonMaterialComponentOperations(
        string materialContainerId,
        PlannedDedicatedMaterialAsset plannedMaterial,
        string batchScopeToken,
        List<DataModelOperation> operations,
        int materialIndex)
    {
        ResoniteMaterialBinding material = plannedMaterial.Material;
        Dictionary<string, Member> materialMembers = ResoniteMaterialComponentPolicy.CreateMembers(material);
        string componentPrefix = $"bootstrap_common_material_component_{materialIndex}";

        Uri? albedoTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(plannedMaterial.Textures, "albedo");
        if (albedoTextureUri is not null)
        {
            ResoniteBatchOperations.PendingBatchComponent albedoTexture = CreatePendingBatchComponent(
                $"{componentPrefix}_albedo",
                StaticTextureComponentType,
                batchScopeToken);
            operations.Add(ResoniteBatchOperations.CreateAddComponentOperation(
                materialContainerId,
                StaticTextureComponentType,
                ResoniteSceneMaterialConventions.CreateTextureMembers(albedoTextureUri),
                albedoTexture));
            materialMembers["AlbedoTexture"] = new Reference
            {
                TargetID = albedoTexture.LocalId,
            };
        }

        Uri? normalTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(plannedMaterial.Textures, "normal");
        if (normalTextureUri is not null)
        {
            ResoniteBatchOperations.PendingBatchComponent normalTexture = CreatePendingBatchComponent(
                $"{componentPrefix}_normal",
                StaticTextureComponentType,
                batchScopeToken);
            operations.Add(ResoniteBatchOperations.CreateAddComponentOperation(
                materialContainerId,
                StaticTextureComponentType,
                ResoniteSceneMaterialConventions.CreateTextureMembers(normalTextureUri),
                normalTexture));
            materialMembers["NormalMap"] = new Reference
            {
                TargetID = normalTexture.LocalId,
            };
            materialMembers["NormalScale"] = new Field_float
            {
                Value = DefaultNormalScale,
            };
        }

        Uri? heightTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(plannedMaterial.Textures, "height");
        if (heightTextureUri is not null)
        {
            ResoniteBatchOperations.PendingBatchComponent heightTexture = CreatePendingBatchComponent(
                $"{componentPrefix}_height",
                StaticTextureComponentType,
                batchScopeToken);
            operations.Add(ResoniteBatchOperations.CreateAddComponentOperation(
                materialContainerId,
                StaticTextureComponentType,
                ResoniteSceneMaterialConventions.CreateTextureMembers(heightTextureUri),
                heightTexture));
            materialMembers["HeightMap"] = new Reference
            {
                TargetID = heightTexture.LocalId,
            };
            materialMembers["HeightScale"] = new Field_float
            {
                Value = DefaultBundledHeightScale,
            };
        }

        Uri? metallicTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(plannedMaterial.Textures, "metallic");
        if (metallicTextureUri is not null)
        {
            ResoniteBatchOperations.PendingBatchComponent metallicTexture = CreatePendingBatchComponent(
                $"{componentPrefix}_metallic",
                StaticTextureComponentType,
                batchScopeToken);
            operations.Add(ResoniteBatchOperations.CreateAddComponentOperation(
                materialContainerId,
                StaticTextureComponentType,
                ResoniteSceneMaterialConventions.CreateTextureMembers(metallicTextureUri),
                metallicTexture));
            materialMembers["MetallicMap"] = new Reference
            {
                TargetID = metallicTexture.LocalId,
            };
            materialMembers["OcclusionMap"] = new Reference
            {
                TargetID = metallicTexture.LocalId,
            };
        }

        Uri? emissionTextureUri = ResoniteMaterialPlanning.TryGetPlannedTextureUri(plannedMaterial.Textures, "emission");
        if (emissionTextureUri is not null)
        {
            ResoniteBatchOperations.PendingBatchComponent emissionTexture = CreatePendingBatchComponent(
                $"{componentPrefix}_emission",
                StaticTextureComponentType,
                batchScopeToken);
            operations.Add(ResoniteBatchOperations.CreateAddComponentOperation(
                materialContainerId,
                StaticTextureComponentType,
                ResoniteSceneMaterialConventions.CreateTextureMembers(emissionTextureUri),
                emissionTexture));
            materialMembers["EmissiveMap"] = new Reference
            {
                TargetID = emissionTexture.LocalId,
            };
            materialMembers["EmissiveColor"] = ResoniteMaterialComponentPolicy.CreateColorMember(
                new ResoniteColor(1.0, 1.0, 1.0, 1.0));
        }

        ResoniteBatchOperations.PendingBatchComponent pendingMaterialComponent = CreatePendingBatchComponent(
            componentPrefix,
            ResoniteMaterialComponentPolicy.GetComponentType(material),
            batchScopeToken);
        operations.Add(ResoniteBatchOperations.CreateAddComponentOperation(
            materialContainerId,
            ResoniteMaterialComponentPolicy.GetComponentType(material),
            materialMembers,
            pendingMaterialComponent));
        return pendingMaterialComponent;
    }

    private static Dictionary<string, Member> CreateDatasetLicenseMembers(
        LicenseAttributionMetadata license)
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

    private static ResoniteBatchOperations.PendingBatchSlot CreatePendingBatchSlot(
        string prefix,
        string slotName,
        string batchScopeToken)
    {
        return new ResoniteBatchOperations.PendingBatchSlot(
            $"{prefix}_{batchScopeToken}",
            $"{prefix}_message_{batchScopeToken}",
            slotName);
    }

    private static ResoniteBatchOperations.PendingBatchComponent CreatePendingBatchComponent(
        string prefix,
        string componentType,
        string batchScopeToken)
    {
        return new ResoniteBatchOperations.PendingBatchComponent(
            $"{prefix}_{batchScopeToken}",
            $"{prefix}_message_{batchScopeToken}",
            componentType);
    }

    private static string CreateBatchScopeToken()
    {
        return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
    }

    private static Slot CreateSlot(CreatedSlot createdSlot, ResoniteFloat3? position = null)
    {
        return new Slot
        {
            ID = createdSlot.SlotId,
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
        LicenseAttributionMetadata License,
        IReadOnlyDictionary<string, Member> Members);
}
