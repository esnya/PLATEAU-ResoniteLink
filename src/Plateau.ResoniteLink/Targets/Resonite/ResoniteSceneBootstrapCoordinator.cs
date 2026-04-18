using System.Security.Cryptography;

using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Targets.Resonite;

internal sealed class ResoniteSceneBootstrapCoordinator : IResoniteSceneBootstrapCoordinator
{
    private const string LicenseComponentType = "[FrooxEngine]FrooxEngine.License";
    private const string StaticTextureComponentType = "[FrooxEngine]FrooxEngine.StaticTexture2D";
    private const float DefaultNormalScale = 1.0f;
    private const float DefaultBundledHeightScale = 0.002f;

    private readonly Func<IResoniteLinkClient, string, CancellationToken, Task<CreatedSlot?>> tryGetDatasetRootAsync;
    private readonly Func<IResoniteLinkClient, string, IReadOnlyDictionary<string, Member>, CancellationToken, Task> updateComponentAsync;
    private readonly IResoniteSceneAnchorResolver sceneAnchorResolver;

    internal ResoniteSceneBootstrapCoordinator(
        Func<IResoniteLinkClient, string, CancellationToken, Task<CreatedSlot?>> tryGetDatasetRootAsync,
        Func<IResoniteLinkClient, string, IReadOnlyDictionary<string, Member>, CancellationToken, Task> updateComponentAsync,
        IResoniteSceneAnchorResolver? sceneAnchorResolver = null)
    {
        this.tryGetDatasetRootAsync = tryGetDatasetRootAsync;
        this.updateComponentAsync = updateComponentAsync;
        this.sceneAnchorResolver = sceneAnchorResolver ?? new ResoniteSceneAnchorResolver();
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
        CreatedSlot? existingDatasetRoot = await tryGetDatasetRootAsync(
            setupClient,
            datasetRootName,
            cancellationToken);

        if (existingDatasetRoot is null)
        {
            return await CreateInitialBootstrapStateAsync(
                setupClient,
                setupInfo.Dataset,
                completionMeshCode,
                setupInfo.DatasetLicense,
                commonMaterials,
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
        Slot? commonSlot = assetsSlot is null
            ? null
            : GetReusableChildSlot(new ResoniteSceneSlotSnapshot(assetsSlot), "Common", assetsSlot.ID!);
        string? existingLicenseComponentId = datasetRootSnapshot.Components?
            .Where(static component => string.Equals(component.ComponentType, LicenseComponentType, StringComparison.Ordinal))
            .OrderBy(static component => component.ID, StringComparer.Ordinal)
            .Select(static component => component.ID)
            .FirstOrDefault(static id => !string.IsNullOrWhiteSpace(id));
        string? datasetLicenseComponentId = existingLicenseComponentId;
        IReadOnlyDictionary<string, Member> datasetLicenseMembers = CreateDatasetLicenseMembers(setupInfo.DatasetLicense);

        List<DataModelOperation> operations = [];
        ResoniteBatchOperations.PendingBatchSlot? pendingAssets = null;
        ResoniteBatchOperations.PendingBatchSlot? pendingCommon = null;
        ResoniteBatchOperations.PendingBatchComponent? pendingLicense = null;
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

        if (commonSlot is null)
        {
            pendingCommon = CreatePendingBatchSlot("bootstrap_common_assets_root", "Common", batchScopeToken);
            operations.Add(ResoniteBatchOperations.CreateAddSlotOperation(assetsParentId, "Common", null, null, pendingCommon.Value));
        }

        string commonParentId = commonSlot?.ID
            ?? pendingCommon?.LocalId
            ?? throw new InvalidOperationException("Bootstrap could not determine the Common parent slot.");

        SceneAnchor sceneAnchor = await sceneAnchorResolver.ResolveAsync(
            setupClient,
            existingDatasetRoot.Value.SlotId,
            completionMeshCode,
            datasetRootExisted: true,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(existingLicenseComponentId))
        {
            pendingLicense = CreatePendingBatchComponent("bootstrap_dataset_license", LicenseComponentType, batchScopeToken);
            operations.Add(ResoniteBatchOperations.CreateAddComponentOperation(existingDatasetRoot.Value.SlotId, LicenseComponentType, datasetLicenseMembers, pendingLicense.Value));
        }

        (Dictionary<string, CreatedMaterialAsset> commonMaterialAssetsByKey, HashSet<string> commonMaterialFamilies, List<PlannedCommonMaterialBatchEntry> plannedCommonMaterials)
            = await PlanCommonMaterialOperationsAsync(
                setupClient,
                commonMaterials,
                commonSlot,
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

            if (pendingCommon is not null)
            {
                commonSlot = CreateSlot(entityMap.ResolveSlot(pendingCommon.Value));
            }

            if (pendingLicense is not null)
            {
                datasetLicenseComponentId = entityMap.ResolveComponent(pendingLicense.Value).ComponentId;
            }

            foreach (PlannedCommonMaterialBatchEntry plannedMaterial in plannedCommonMaterials)
            {
                commonMaterialAssetsByKey[plannedMaterial.MaterialKey] = new CreatedMaterialAsset(
                    entityMap.ResolveComponent(plannedMaterial.PendingMaterialComponent).ComponentId,
                    null);
            }
        }

        if (assetsSlot is null || commonSlot is null)
        {
            throw new InvalidOperationException("Bootstrap did not resolve required shared slots.");
        }

        if (!string.IsNullOrWhiteSpace(existingLicenseComponentId))
        {
            await updateComponentAsync(
                setupClient,
                existingLicenseComponentId,
                datasetLicenseMembers,
                cancellationToken);
        }

        return new ResoniteSceneBootstrapState(
            existingDatasetRoot.Value,
            new CreatedSlot(assetsSlot.ID!, assetsSlot.Name?.Value ?? "Assets"),
            new CreatedSlot(commonSlot.ID!, commonSlot.Name?.Value ?? "Common"),
            DatasetRootExisted: true,
            sceneAnchor,
            datasetRootSnapshot,
            existingLicenseComponentId,
            datasetLicenseComponentId,
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

    public async Task<string> ApplyDatasetLicenseAsync(
        IResoniteLinkClient setupClient,
        string datasetRootSlotId,
        ResoniteLicenseComponentMetadata license,
        string? existingComponentId,
        bool allowUpdateExisting,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(setupClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetRootSlotId);
        ArgumentNullException.ThrowIfNull(license);

        IReadOnlyDictionary<string, Member> members = CreateDatasetLicenseMembers(license);
        if (!string.IsNullOrWhiteSpace(existingComponentId))
        {
            await updateComponentAsync(
                setupClient,
                existingComponentId,
                members,
                cancellationToken);
            return existingComponentId;
        }

        ResoniteBatchOperations.PendingBatchComponent pendingLicense = CreatePendingBatchComponent(
            "bootstrap_dataset_license",
            LicenseComponentType,
            CreateBatchScopeToken());
        BatchResponse response = await setupClient.RunDataModelOperationBatchAsync(
            [
                ResoniteBatchOperations.CreateAddComponentOperation(datasetRootSlotId, LicenseComponentType, members, pendingLicense),
            ],
            cancellationToken);
        CanonicalBatchEntityMap entityMap = CanonicalBatchEntityMap.Create(response);
        return entityMap.ResolveComponent(pendingLicense).ComponentId;
    }

    private static async Task<ResoniteSceneBootstrapState> CreateInitialBootstrapStateAsync(
        IResoniteLinkClient setupClient,
        string datasetName,
        string completionMeshCode,
        ResoniteLicenseComponentMetadata datasetLicense,
        IReadOnlyList<ResoniteMaterialBinding> commonMaterials,
        CancellationToken cancellationToken)
    {
        string datasetRootName = $"PLATEAU {datasetName}";
        ResoniteFloat3 anchorPosition = new(0.0, 0.0, 0.0);
        string batchScopeToken = CreateBatchScopeToken();
        ResoniteBatchOperations.PendingBatchSlot pendingDatasetRootSlot = CreatePendingBatchSlot("bootstrap_dataset_root", datasetRootName, batchScopeToken);
        ResoniteBatchOperations.PendingBatchSlot pendingDatasetAssetsRootSlot = CreatePendingBatchSlot("bootstrap_assets_root", "Assets", batchScopeToken);
        ResoniteBatchOperations.PendingBatchSlot pendingCommonAssetsRootSlot = CreatePendingBatchSlot("bootstrap_common_assets_root", "Common", batchScopeToken);
        ResoniteBatchOperations.PendingBatchComponent pendingLicense = CreatePendingBatchComponent("bootstrap_dataset_license", LicenseComponentType, batchScopeToken);
        List<DataModelOperation> operations =
        [
            ResoniteBatchOperations.CreateAddSlotOperation("Root", datasetRootName, null, null, pendingDatasetRootSlot),
            ResoniteBatchOperations.CreateAddSlotOperation(pendingDatasetRootSlot.LocalId, "Assets", null, null, pendingDatasetAssetsRootSlot),
            ResoniteBatchOperations.CreateAddSlotOperation(pendingDatasetAssetsRootSlot.LocalId, "Common", null, null, pendingCommonAssetsRootSlot),
            ResoniteBatchOperations.CreateAddComponentOperation(
                pendingDatasetRootSlot.LocalId,
                LicenseComponentType,
                CreateDatasetLicenseMembers(datasetLicense),
                pendingLicense),
        ];

        (Dictionary<string, CreatedMaterialAsset> commonMaterialAssetsByKey, HashSet<string> commonMaterialFamilies, List<PlannedCommonMaterialBatchEntry> plannedCommonMaterials)
            = await PlanCommonMaterialOperationsAsync(
                setupClient,
                commonMaterials,
                commonSlot: null,
                commonParentId: pendingCommonAssetsRootSlot.LocalId,
                batchScopeToken,
                operations,
                cancellationToken);

        BatchResponse response = await setupClient.RunDataModelOperationBatchAsync(operations, cancellationToken);

        CanonicalBatchEntityMap entityMap = CanonicalBatchEntityMap.Create(response);
        CreatedSlot datasetRootSlot = entityMap.ResolveSlot(pendingDatasetRootSlot);
        CreatedSlot datasetAssetsRootSlot = entityMap.ResolveSlot(pendingDatasetAssetsRootSlot);
        CreatedSlot commonAssetsRootSlot = entityMap.ResolveSlot(pendingCommonAssetsRootSlot);
        CreatedComponent licenseComponent = entityMap.ResolveComponent(pendingLicense);
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
            ExistingLicenseComponentId: null,
            DatasetLicenseComponentId: licenseComponent.ComponentId,
            commonMaterialAssetsByKey,
            commonMaterialFamilies);
    }

    private static async Task<(Dictionary<string, CreatedMaterialAsset> CommonMaterialAssetsByKey, HashSet<string> CommonMaterialFamilies, List<PlannedCommonMaterialBatchEntry> PlannedCommonMaterials)> PlanCommonMaterialOperationsAsync(
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

        foreach (ResoniteMaterialBinding material in canonicalMaterialsByKey.Values.OrderBy(static material => material.MaterialKey, StringComparer.Ordinal))
        {
            string family = material.Family ?? BundledDefaultMaterialFamilies.Other;
            commonMaterialFamilies.Add(family);
            string materialSlotName = ResoniteSceneMaterialConventions.CreateMaterialSlotName(material, useCommonMaterialAssets: true);
            Slot? existingMaterialSlot = commonSlotSnapshot is null
                ? null
                : GetReusableChildSlot(commonSlotSnapshot, materialSlotName, commonSlot!.ID!);
            string materialComponentType = ResoniteMaterialComponentBuilder.GetComponentType(material);
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

            PlannedDedicatedMaterialAsset plannedMaterial = await ResoniteMaterialPlanning.PlanCommonMaterialAssetAsync(
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
                operations.Add(ResoniteBatchOperations.CreateAddSlotOperation(commonParentId, materialSlotName, null, null, pendingMaterialSlot.Value));
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
        Dictionary<string, Member> materialMembers = ResoniteMaterialComponentBuilder.CreateMembers(material);
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
            materialMembers["EmissiveColor"] = ResoniteMaterialComponentBuilder.CreateColorMember(
                new ResoniteColor(1.0, 1.0, 1.0, 1.0));
        }

        ResoniteBatchOperations.PendingBatchComponent pendingMaterialComponent = CreatePendingBatchComponent(
            componentPrefix,
            ResoniteMaterialComponentBuilder.GetComponentType(material),
            batchScopeToken);
        operations.Add(ResoniteBatchOperations.CreateAddComponentOperation(
            materialContainerId,
            ResoniteMaterialComponentBuilder.GetComponentType(material),
            materialMembers,
            pendingMaterialComponent));
        return pendingMaterialComponent;
    }

    private static Dictionary<string, Member> CreateDatasetLicenseMembers(
        ResoniteLicenseComponentMetadata license)
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
}
