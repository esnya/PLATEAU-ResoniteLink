using System.Security.Cryptography;

using GeographicLib;

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

    internal ResoniteSceneBootstrapCoordinator(
        Func<IResoniteLinkClient, string, CancellationToken, Task<CreatedSlot?>> tryGetDatasetRootAsync,
        Func<IResoniteLinkClient, string, IReadOnlyDictionary<string, Member>, CancellationToken, Task> updateComponentAsync)
    {
        this.tryGetDatasetRootAsync = tryGetDatasetRootAsync;
        this.updateComponentAsync = updateComponentAsync;
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
        Slot? anchorSlot = GetReusableChildSlot(snapshot, completionMeshCode, existingDatasetRoot.Value.SlotId);
        string? existingLicenseComponentId = datasetRootSnapshot.Components?
            .Where(static component => string.Equals(component.ComponentType, LicenseComponentType, StringComparison.Ordinal))
            .OrderBy(static component => component.ID, StringComparer.Ordinal)
            .Select(static component => component.ID)
            .FirstOrDefault(static id => !string.IsNullOrWhiteSpace(id));
        string? datasetLicenseComponentId = existingLicenseComponentId;
        IReadOnlyDictionary<string, Member> datasetLicenseMembers = CreateDatasetLicenseMembers(setupInfo.DatasetLicense);

        List<DataModelOperation> operations = [];
        PendingBatchSlot? pendingAssets = null;
        PendingBatchSlot? pendingCommon = null;
        PendingBatchSlot? pendingAnchor = null;
        PendingBatchComponent? pendingLicense = null;
        string batchScopeToken = CreateBatchScopeToken();

        string assetsParentId = existingDatasetRoot.Value.SlotId;
        if (assetsSlot is null)
        {
            pendingAssets = CreatePendingBatchSlot("bootstrap_assets_root", "Assets", batchScopeToken);
            operations.Add(CreateAddSlotOperation(existingDatasetRoot.Value.SlotId, "Assets", null, null, pendingAssets.Value));
            assetsParentId = pendingAssets.Value.LocalId;
        }
        else
        {
            assetsParentId = assetsSlot.ID ?? throw new InvalidOperationException("Existing Assets slot did not expose an ID.");
        }

        if (commonSlot is null)
        {
            pendingCommon = CreatePendingBatchSlot("bootstrap_common_assets_root", "Common", batchScopeToken);
            operations.Add(CreateAddSlotOperation(assetsParentId, "Common", null, null, pendingCommon.Value));
        }

        string commonParentId = commonSlot?.ID
            ?? pendingCommon?.LocalId
            ?? throw new InvalidOperationException("Bootstrap could not determine the Common parent slot.");

        ResoniteFloat3 anchorPosition = ResolveAnchorPosition(datasetRootSnapshot, completionMeshCode);
        if (anchorSlot is null)
        {
            pendingAnchor = CreatePendingBatchSlot("bootstrap_anchor_root", completionMeshCode, batchScopeToken);
            operations.Add(CreateAddSlotOperation(existingDatasetRoot.Value.SlotId, completionMeshCode, anchorPosition, null, pendingAnchor.Value));
        }

        if (string.IsNullOrWhiteSpace(existingLicenseComponentId))
        {
            pendingLicense = CreatePendingBatchComponent("bootstrap_dataset_license", LicenseComponentType, batchScopeToken);
            operations.Add(CreateAddComponentOperation(existingDatasetRoot.Value.SlotId, LicenseComponentType, datasetLicenseMembers, pendingLicense.Value));
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

            if (pendingAnchor is not null)
            {
                anchorSlot = CreateSlot(entityMap.ResolveSlot(pendingAnchor.Value), anchorPosition);
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

        if (assetsSlot is null || commonSlot is null || anchorSlot is null)
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
            new SceneAnchor(anchorSlot.ID!, completionMeshCode, GetSlotPosition(anchorSlot)),
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

        PendingBatchComponent pendingLicense = CreatePendingBatchComponent(
            "bootstrap_dataset_license",
            LicenseComponentType,
            CreateBatchScopeToken());
        BatchResponse response = await setupClient.RunDataModelOperationBatchAsync(
            [
                CreateAddComponentOperation(datasetRootSlotId, LicenseComponentType, members, pendingLicense),
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
        PendingBatchSlot pendingDatasetRootSlot = CreatePendingBatchSlot("bootstrap_dataset_root", datasetRootName, batchScopeToken);
        PendingBatchSlot pendingDatasetAssetsRootSlot = CreatePendingBatchSlot("bootstrap_assets_root", "Assets", batchScopeToken);
        PendingBatchSlot pendingCommonAssetsRootSlot = CreatePendingBatchSlot("bootstrap_common_assets_root", "Common", batchScopeToken);
        PendingBatchSlot pendingAnchorSlot = CreatePendingBatchSlot("bootstrap_anchor_root", completionMeshCode, batchScopeToken);
        PendingBatchComponent pendingLicense = CreatePendingBatchComponent("bootstrap_dataset_license", LicenseComponentType, batchScopeToken);
        List<DataModelOperation> operations =
        [
            CreateAddSlotOperation("Root", datasetRootName, null, null, pendingDatasetRootSlot),
            CreateAddSlotOperation(pendingDatasetRootSlot.LocalId, "Assets", null, null, pendingDatasetAssetsRootSlot),
            CreateAddSlotOperation(pendingDatasetAssetsRootSlot.LocalId, "Common", null, null, pendingCommonAssetsRootSlot),
            CreateAddSlotOperation(pendingDatasetRootSlot.LocalId, completionMeshCode, anchorPosition, null, pendingAnchorSlot),
            CreateAddComponentOperation(
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
        CreatedSlot anchorSlot = entityMap.ResolveSlot(pendingAnchorSlot);
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
            new SceneAnchor(anchorSlot.SlotId, completionMeshCode, anchorPosition),
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
            string materialSlotName = ResoniteLinkSceneBuilder.CreateMaterialSlotName(material, useCommonMaterialAssets: true);
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

            PreparedBootstrapCommonMaterialAssets preparedAssets = await PrepareCommonMaterialAssetsAsync(
                setupClient,
                material,
                cancellationToken);
            int materialIndex = plannedCommonMaterials.Count;
            PendingBatchSlot? pendingMaterialSlot = null;
            string materialContainerId = existingMaterialSlot?.ID ?? string.Empty;
            if (existingMaterialSlot is null)
            {
                pendingMaterialSlot = CreatePendingBatchSlot(
                    $"bootstrap_common_material_slot_{materialIndex}",
                    materialSlotName,
                    batchScopeToken);
                materialContainerId = pendingMaterialSlot.Value.LocalId;
                operations.Add(CreateAddSlotOperation(commonParentId, materialSlotName, null, null, pendingMaterialSlot.Value));
            }

            PendingBatchComponent pendingMaterialComponent = AddCommonMaterialComponentOperations(
                materialContainerId,
                material,
                preparedAssets,
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
            ResoniteMaterialBinding normalizedMaterial = ResoniteLinkSceneBuilder.NormalizeCommonMaterialBinding(material);
            if (normalizedMaterial.AssetScope != ResoniteMaterialAssetScope.Common)
            {
                continue;
            }

            canonicalMaterialsByKey.TryAdd(normalizedMaterial.MaterialKey, normalizedMaterial);
        }

        return canonicalMaterialsByKey;
    }

    private static async Task<PreparedBootstrapCommonMaterialAssets> PrepareCommonMaterialAssetsAsync(
        IResoniteLinkClient setupClient,
        ResoniteMaterialBinding material,
        CancellationToken cancellationToken)
    {
        Task<Uri?> albedoTextureTask = Task.FromResult<Uri?>(null);
        if (!string.IsNullOrWhiteSpace(material.Family))
        {
            string albedoPath = BundledDefaultMaterialAssetStore.GetAbsolutePath(
                BundledDefaultMaterialFamilies.GetVariant(material.Family!, material.BundledVariantIndex ?? 0));
            ResoniteRawTextureImport albedoTexture = await ResoniteTextureImportFactory.CreateRawFromFileAsync(
                albedoPath,
                ResoniteTextureColorProfiles.Srgb,
                cancellationToken);
            albedoTextureTask = ImportOptionalTextureAsync(setupClient, albedoTexture, cancellationToken);
        }

        Task<Uri?> normalTextureTask = Task.FromResult<Uri?>(null);
        Task<Uri?> heightTextureTask = Task.FromResult<Uri?>(null);
        Task<Uri?> metallicTextureTask = Task.FromResult<Uri?>(null);
        Task<Uri?> emissionTextureTask = Task.FromResult<Uri?>(null);

        if (ResoniteMaterialComponentBuilder.TryGetBundledCompanionTextureSet(material, out BundledDefaultMaterialTextureSet? textureSet)
            && textureSet is not null)
        {
            if (textureSet.NormalPath is not null)
            {
                normalTextureTask = ImportOptionalTextureAsync(
                    setupClient,
                    await ResoniteTextureImportFactory.CreateRawFromFileAsync(
                        textureSet.NormalPath,
                        ResoniteTextureColorProfiles.Linear,
                        cancellationToken),
                    cancellationToken);
            }

            if (textureSet.HeightPath is not null
                && material.Projection == ResoniteMaterialProjection.Uv)
            {
                heightTextureTask = ImportOptionalTextureAsync(
                    setupClient,
                    await ResoniteTextureImportFactory.CreateRawFromFileAsync(
                        textureSet.HeightPath,
                        ResoniteTextureColorProfiles.Linear,
                        cancellationToken),
                    cancellationToken);
            }

            if (textureSet.MetallicPath is not null)
            {
                metallicTextureTask = ImportOptionalTextureAsync(
                    setupClient,
                    await ResoniteTextureImportFactory.CreateRawFromFileAsync(
                        textureSet.MetallicPath,
                        ResoniteTextureColorProfiles.Linear,
                        cancellationToken),
                    cancellationToken);
            }

            if (textureSet.EmissionPath is not null)
            {
                emissionTextureTask = ImportOptionalTextureAsync(
                    setupClient,
                    await ResoniteTextureImportFactory.CreateRawFromFileAsync(
                        textureSet.EmissionPath,
                        ResoniteTextureColorProfiles.Srgb,
                        cancellationToken),
                    cancellationToken);
            }
        }

        await Task.WhenAll(albedoTextureTask, normalTextureTask, heightTextureTask, metallicTextureTask, emissionTextureTask);
        return new PreparedBootstrapCommonMaterialAssets(
            await albedoTextureTask,
            await normalTextureTask,
            await heightTextureTask,
            await metallicTextureTask,
            await emissionTextureTask);
    }

    private static Task<Uri?> ImportOptionalTextureAsync(
        IResoniteLinkClient setupClient,
        ResoniteTextureImport textureImport,
        CancellationToken cancellationToken)
    {
        return ImportOptionalTextureCoreAsync(setupClient, textureImport, cancellationToken);
    }

    private static async Task<Uri?> ImportOptionalTextureCoreAsync(
        IResoniteLinkClient setupClient,
        ResoniteTextureImport textureImport,
        CancellationToken cancellationToken)
    {
        return await setupClient.ImportTextureAsync(textureImport, cancellationToken);
    }

    private static PendingBatchComponent AddCommonMaterialComponentOperations(
        string materialContainerId,
        ResoniteMaterialBinding material,
        PreparedBootstrapCommonMaterialAssets preparedAssets,
        string batchScopeToken,
        List<DataModelOperation> operations,
        int materialIndex)
    {
        Dictionary<string, Member> materialMembers = ResoniteMaterialComponentBuilder.CreateMembers(material);
        string componentPrefix = $"bootstrap_common_material_component_{materialIndex}";

        if (preparedAssets.AlbedoTextureUri is not null)
        {
            PendingBatchComponent albedoTexture = CreatePendingBatchComponent(
                $"{componentPrefix}_albedo",
                StaticTextureComponentType,
                batchScopeToken);
            operations.Add(CreateAddComponentOperation(
                materialContainerId,
                StaticTextureComponentType,
                ResoniteLinkSceneBuilder.CreateTextureMembers(preparedAssets.AlbedoTextureUri),
                albedoTexture));
            materialMembers["AlbedoTexture"] = new Reference
            {
                TargetID = albedoTexture.LocalId,
            };
        }

        if (preparedAssets.NormalTextureUri is not null)
        {
            PendingBatchComponent normalTexture = CreatePendingBatchComponent(
                $"{componentPrefix}_normal",
                StaticTextureComponentType,
                batchScopeToken);
            operations.Add(CreateAddComponentOperation(
                materialContainerId,
                StaticTextureComponentType,
                ResoniteLinkSceneBuilder.CreateTextureMembers(preparedAssets.NormalTextureUri),
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

        if (preparedAssets.HeightTextureUri is not null)
        {
            PendingBatchComponent heightTexture = CreatePendingBatchComponent(
                $"{componentPrefix}_height",
                StaticTextureComponentType,
                batchScopeToken);
            operations.Add(CreateAddComponentOperation(
                materialContainerId,
                StaticTextureComponentType,
                ResoniteLinkSceneBuilder.CreateTextureMembers(preparedAssets.HeightTextureUri),
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

        if (preparedAssets.MetallicTextureUri is not null)
        {
            PendingBatchComponent metallicTexture = CreatePendingBatchComponent(
                $"{componentPrefix}_metallic",
                StaticTextureComponentType,
                batchScopeToken);
            operations.Add(CreateAddComponentOperation(
                materialContainerId,
                StaticTextureComponentType,
                ResoniteLinkSceneBuilder.CreateTextureMembers(preparedAssets.MetallicTextureUri),
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

        if (preparedAssets.EmissionTextureUri is not null)
        {
            PendingBatchComponent emissionTexture = CreatePendingBatchComponent(
                $"{componentPrefix}_emission",
                StaticTextureComponentType,
                batchScopeToken);
            operations.Add(CreateAddComponentOperation(
                materialContainerId,
                StaticTextureComponentType,
                ResoniteLinkSceneBuilder.CreateTextureMembers(preparedAssets.EmissionTextureUri),
                emissionTexture));
            materialMembers["EmissiveMap"] = new Reference
            {
                TargetID = emissionTexture.LocalId,
            };
            materialMembers["EmissiveColor"] = ResoniteMaterialComponentBuilder.CreateColorMember(
                new ResoniteColor(1.0, 1.0, 1.0, 1.0));
        }

        PendingBatchComponent pendingMaterialComponent = CreatePendingBatchComponent(
            componentPrefix,
            ResoniteMaterialComponentBuilder.GetComponentType(material),
            batchScopeToken);
        operations.Add(CreateAddComponentOperation(
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

    private static PendingBatchSlot CreatePendingBatchSlot(
        string prefix,
        string slotName,
        string batchScopeToken)
    {
        return new PendingBatchSlot(
            $"{prefix}_{batchScopeToken}",
            $"{prefix}_message_{batchScopeToken}",
            slotName);
    }

    private static PendingBatchComponent CreatePendingBatchComponent(
        string prefix,
        string componentType,
        string batchScopeToken)
    {
        return new PendingBatchComponent(
            $"{prefix}_{batchScopeToken}",
            $"{prefix}_message_{batchScopeToken}",
            componentType);
    }

    private static string CreateBatchScopeToken()
    {
        return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
    }

    private static AddSlot CreateAddSlotOperation(
        string parentId,
        string slotName,
        ResoniteFloat3? position,
        ResoniteFloatQ? rotation,
        PendingBatchSlot pendingSlot)
    {
        return new AddSlot
        {
            MessageID = pendingSlot.MessageId,
            Data = new Slot
            {
                ID = pendingSlot.LocalId,
                Parent = new Reference
                {
                    TargetID = parentId,
                },
                Name = new Field_string
                {
                    Value = slotName,
                },
                Position = position is null ? null : CreateFloat3(position),
                Rotation = rotation is null ? null : CreateFloatQ(rotation),
            },
        };
    }

    private static AddComponent CreateAddComponentOperation(
        string containerSlotId,
        string componentType,
        IReadOnlyDictionary<string, Member> members,
        PendingBatchComponent pendingComponent)
    {
        return new AddComponent
        {
            MessageID = pendingComponent.MessageId,
            ContainerSlotId = containerSlotId,
            Data = new Component
            {
                ID = pendingComponent.LocalId,
                ComponentType = componentType,
                Members = members.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal),
            },
        };
    }

    private static ResoniteFloat3 ResolveAnchorPosition(Slot datasetRootSlot, string completionMeshCode)
    {
        Slot? referenceMeshRoot = datasetRootSlot.Children?
            .Where(static child => !string.Equals(child.Name?.Value, "Assets", StringComparison.Ordinal))
            .Where(static child => !string.IsNullOrWhiteSpace(child.Name?.Value))
            .Where(static child => PlateauMeshCode.TryGetCenter(child.Name!.Value, out _))
            .OrderBy(static child => child.Name!.Value, StringComparer.Ordinal)
            .FirstOrDefault();
        if (referenceMeshRoot is null)
        {
            return new ResoniteFloat3(0.0, 0.0, 0.0);
        }

        return Add(
            GetSlotPosition(referenceMeshRoot),
            ComputeMeshCodeOffset(referenceMeshRoot.Name!.Value, completionMeshCode));
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
            Position = position is null ? null : CreateFloat3(position),
        };
    }

    private static ResoniteFloat3 GetSlotPosition(Slot slot)
    {
        if (slot.Position is Field_float3 position)
        {
            return new ResoniteFloat3(position.Value.x, position.Value.y, position.Value.z);
        }

        return new ResoniteFloat3(0.0, 0.0, 0.0);
    }

    private static ResoniteFloat3 ComputeMeshCodeOffset(string referenceMeshCode, string meshCode)
    {
        if (!PlateauMeshCode.TryGetCenter(referenceMeshCode, out ResoniteLocalOrigin referenceCenter)
            || !PlateauMeshCode.TryGetCenter(meshCode, out ResoniteLocalOrigin currentCenter))
        {
            return new ResoniteFloat3(0.0, 0.0, 0.0);
        }

        return ComputeOriginOffset(referenceCenter, currentCenter);
    }

    private static ResoniteFloat3 ComputeOriginOffset(
        ResoniteLocalOrigin referenceCenter,
        ResoniteLocalOrigin currentCenter)
    {
        LocalCartesian cartesian = new(
            referenceCenter.Latitude,
            referenceCenter.Longitude,
            referenceCenter.Altitude,
            Geocentric.WGS84);
        (double x, double y, double z) eun = cartesian.Forward(
            currentCenter.Latitude,
            currentCenter.Longitude,
            currentCenter.Altitude);
        return new ResoniteFloat3(
            X: eun.x,
            Y: 0.0,
            Z: eun.y);
    }

    private static ResoniteFloat3 Add(ResoniteFloat3 left, ResoniteFloat3 right)
    {
        return new ResoniteFloat3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    }

    private static Field_float3 CreateFloat3(ResoniteFloat3 value)
    {
        return new Field_float3
        {
            Value = new float3
            {
                x = (float)value.X,
                y = (float)value.Y,
                z = (float)value.Z,
            },
        };
    }

    private static Field_floatQ CreateFloatQ(ResoniteFloatQ value)
    {
        return new Field_floatQ
        {
            Value = new floatQ
            {
                x = (float)value.X,
                y = (float)value.Y,
                z = (float)value.Z,
                w = (float)value.W,
            },
        };
    }

    private readonly record struct PendingBatchSlot(
        string LocalId,
        string MessageId,
        string SlotName);

    private readonly record struct PendingBatchComponent(
        string LocalId,
        string MessageId,
        string ComponentType);

    private readonly record struct PlannedCommonMaterialBatchEntry(
        string MaterialKey,
        string Family,
        PendingBatchComponent PendingMaterialComponent);

    private readonly record struct PreparedBootstrapCommonMaterialAssets(
        Uri? AlbedoTextureUri,
        Uri? NormalTextureUri,
        Uri? HeightTextureUri,
        Uri? MetallicTextureUri,
        Uri? EmissionTextureUri);

    private sealed class CanonicalBatchEntityMap
    {
        private readonly Dictionary<string, Response> responsesByMessageId;
        private readonly Queue<Response> responsesWithoutMessageId;

        private CanonicalBatchEntityMap(
            Dictionary<string, Response> responsesByMessageId,
            Queue<Response> responsesWithoutMessageId)
        {
            this.responsesByMessageId = responsesByMessageId;
            this.responsesWithoutMessageId = responsesWithoutMessageId;
        }

        public static CanonicalBatchEntityMap Create(BatchResponse batchResponse)
        {
            ArgumentNullException.ThrowIfNull(batchResponse);
            return new CanonicalBatchEntityMap(
                (batchResponse.Responses ?? [])
                    .Where(static response => !string.IsNullOrWhiteSpace(response.SourceMessageID))
                    .ToDictionary(response => response.SourceMessageID, StringComparer.Ordinal),
                new Queue<Response>(
                    (batchResponse.Responses ?? [])
                        .Where(static response => string.IsNullOrWhiteSpace(response.SourceMessageID))));
        }

        public CreatedSlot ResolveSlot(PendingBatchSlot pendingSlot)
        {
            Response response = ResolveResponse(pendingSlot.MessageId);
            if (response is not NewEntityId newEntityId || string.IsNullOrWhiteSpace(newEntityId.EntityId))
            {
                throw new InvalidOperationException(
                    $"Batch response for slot '{pendingSlot.SlotName}' did not include a canonical slot ID.");
            }

            return new CreatedSlot(newEntityId.EntityId, pendingSlot.SlotName);
        }

        public CreatedComponent ResolveComponent(PendingBatchComponent pendingComponent)
        {
            Response response = ResolveResponse(pendingComponent.MessageId);
            if (response is not NewEntityId newEntityId || string.IsNullOrWhiteSpace(newEntityId.EntityId))
            {
                throw new InvalidOperationException(
                    $"Batch response for component '{pendingComponent.ComponentType}' did not include a canonical component ID.");
            }

            return new CreatedComponent(newEntityId.EntityId, pendingComponent.ComponentType);
        }

        private Response ResolveResponse(string messageId)
        {
            if (!responsesByMessageId.TryGetValue(messageId, out Response? response))
            {
                if (responsesWithoutMessageId.Count == 0)
                {
                    throw new InvalidOperationException($"Batch response did not include message '{messageId}'.");
                }

                response = responsesWithoutMessageId.Dequeue();
            }

            ResoniteLinkClient.EnsureSuccess(response, $"resolve batch message '{messageId}'");
            return response;
        }
    }
}
