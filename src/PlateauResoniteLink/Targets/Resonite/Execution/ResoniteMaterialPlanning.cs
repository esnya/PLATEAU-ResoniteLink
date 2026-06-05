using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite.Execution;

internal interface IResoniteMaterialPlanning
{
    Task<PlannedDedicatedMaterialAsset> PlanCommonMaterialAssetAsync(
        IResoniteLinkClient importClient,
        ResoniteMaterialBinding material,
        AsyncInFlightResultCache<BundledTextureImportKey, Uri> bundledTextureImportTasks,
        CancellationToken cancellationToken);

    Task<PlannedDedicatedMaterialAsset> PlanDedicatedMaterialAssetAsync(
        IResoniteLinkClient importClient,
        ResoniteMaterialBinding material,
        int materialIndex,
        IReadOnlyDictionary<ResoniteTexturePayload, Uri> preparedTextureUrisByPayload,
        IReadOnlyDictionary<TerrainTextureOverlay, Uri> preparedTerrainTextureUrisByOverlay,
        bool preserveDedicatedMaterialSlot,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteMaterialPlanning : IResoniteMaterialPlanning
{
    private const float DefaultNormalScale = 1.0f;
    private const float DefaultBundledHeightScale = 0.002f;
    private readonly BundledDefaultMaterialAssetStore bundledDefaultMaterialAssetStore;

    public ResoniteMaterialPlanning(BundledDefaultMaterialAssetStore bundledDefaultMaterialAssetStore)
    {
        this.bundledDefaultMaterialAssetStore =
            bundledDefaultMaterialAssetStore ?? throw new ArgumentNullException(nameof(bundledDefaultMaterialAssetStore));
    }

    public async Task<PlannedDedicatedMaterialAsset> PlanCommonMaterialAssetAsync(
        IResoniteLinkClient importClient,
        ResoniteMaterialBinding material,
        AsyncInFlightResultCache<BundledTextureImportKey, Uri> bundledTextureImportTasks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(importClient);
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(bundledTextureImportTasks);

        Task<Uri?> albedoTextureTask = Task.FromResult<Uri?>(null);
        if (!string.IsNullOrWhiteSpace(material.Family))
        {
            albedoTextureTask = ImportBundledTextureAsync(
                importClient,
                GetBundledAlbedoTextureAsset(material),
                ResoniteTextureColorProfiles.Srgb,
                bundledTextureImportTasks,
                cancellationToken);
        }

        List<PlannedTextureAsset> textures = await PlanBundledCompanionTexturesAsync(
            importClient,
            material,
            albedoTextureTask,
            bundledTextureImportTasks,
            cancellationToken);
        return new PlannedDedicatedMaterialAsset(
            material,
            textures,
            PreserveDedicatedMaterialSlot: false);
    }

    public async Task<PlannedDedicatedMaterialAsset> PlanDedicatedMaterialAssetAsync(
        IResoniteLinkClient importClient,
        ResoniteMaterialBinding material,
        int materialIndex,
        IReadOnlyDictionary<ResoniteTexturePayload, Uri> preparedTextureUrisByPayload,
        IReadOnlyDictionary<TerrainTextureOverlay, Uri> preparedTerrainTextureUrisByOverlay,
        bool preserveDedicatedMaterialSlot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(importClient);
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(preparedTextureUrisByPayload);
        ArgumentNullException.ThrowIfNull(preparedTerrainTextureUrisByOverlay);

        Task<Uri?> albedoTextureTask = material.TexturePayload is not null
            && preparedTextureUrisByPayload.TryGetValue(material.TexturePayload, out Uri? directTextureUri)
            ? Task.FromResult<Uri?>(directTextureUri)
            : material.TerrainOverlay is not null
            && preparedTerrainTextureUrisByOverlay.TryGetValue(
                material.TerrainOverlay,
                out Uri? terrainOverlayTextureUri)
            ? Task.FromResult<Uri?>(terrainOverlayTextureUri)
            : !string.IsNullOrWhiteSpace(material.Family)
            ? ImportBundledAlbedoTextureAsync(importClient, material, cancellationToken)
            : Task.FromResult<Uri?>(null);

        List<PlannedTextureAsset> textures = await PlanBundledCompanionTexturesAsync(
            importClient,
            material,
            albedoTextureTask,
            bundledTextureImportTasks: null,
            cancellationToken);
        return new PlannedDedicatedMaterialAsset(
            material,
            textures,
            preserveDedicatedMaterialSlot,
            DedicatedMaterialSlotName: preserveDedicatedMaterialSlot
                ? ResoniteSceneMaterialConventions.CreateDedicatedMaterialSlotName(material, materialIndex)
                : null);
    }

    public static Uri? TryGetPlannedTextureUri(
        IEnumerable<PlannedTextureAsset> textures,
        ResoniteSceneMaterialConventions.TextureMemberRole role)
    {
        ArgumentNullException.ThrowIfNull(textures);

        TextureIdentity identity = ResoniteSceneMaterialConventions.CreateTextureIdentity(role);
        return textures.FirstOrDefault(texture => texture.Identity == identity)?.AssetUri;
    }

    public static PlannedTextureAsset? PlanMainTextureOverride(
        ResoniteMaterialBinding material,
        IReadOnlyDictionary<ResoniteTexturePayload, Uri> preparedTextureUrisByPayload,
        IReadOnlyDictionary<TerrainTextureOverlay, Uri> preparedTerrainTextureUrisByOverlay)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(preparedTextureUrisByPayload);
        ArgumentNullException.ThrowIfNull(preparedTerrainTextureUrisByOverlay);

        Uri? textureUri = material.TexturePayload is not null
            && preparedTextureUrisByPayload.TryGetValue(material.TexturePayload, out Uri? directTextureUri)
                ? directTextureUri
            : material.TerrainOverlay is not null
            && preparedTerrainTextureUrisByOverlay.TryGetValue(material.TerrainOverlay, out Uri? terrainOverlayTextureUri)
                ? terrainOverlayTextureUri
            : null;
        if (textureUri is null)
        {
            return null;
        }

        return new PlannedTextureAsset(
            new TextureIdentity("main"),
            textureUri);
    }

    public static ResoniteBatchOperations.PendingBatchComponent AddCommonMaterialComponents(
        ResoniteBatchOperations.BatchActionBuilder batchBuilder,
        PlannedDedicatedMaterialAsset plannedMaterial,
        string materialContainerSlotId)
    {
        ArgumentNullException.ThrowIfNull(batchBuilder);
        ArgumentNullException.ThrowIfNull(plannedMaterial);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialContainerSlotId);

        Dictionary<string, Member> materialMembers = ResoniteMaterialComponentPolicy.CreateMembers(plannedMaterial.Material);

        AddTextureComponentReference(
            batchBuilder,
            plannedMaterial,
            materialContainerSlotId,
            ResoniteSceneMaterialConventions.TextureMemberRole.Albedo,
            "AlbedoTexture",
            materialMembers);
        if (AddTextureComponentReference(
                batchBuilder,
                plannedMaterial,
                materialContainerSlotId,
                ResoniteSceneMaterialConventions.TextureMemberRole.Normal,
                "NormalMap",
                materialMembers))
        {
            materialMembers["NormalScale"] = new Field_float
            {
                Value = DefaultNormalScale,
            };
        }

        if (AddTextureComponentReference(
                batchBuilder,
                plannedMaterial,
                materialContainerSlotId,
                ResoniteSceneMaterialConventions.TextureMemberRole.Height,
                "HeightMap",
                materialMembers))
        {
            materialMembers["HeightScale"] = new Field_float
            {
                Value = DefaultBundledHeightScale,
            };
        }

        Uri? metallicTextureUri = TryGetPlannedTextureUri(
            plannedMaterial.Textures,
            ResoniteSceneMaterialConventions.TextureMemberRole.Metallic);
        if (metallicTextureUri is not null)
        {
            ResoniteBatchOperations.PendingBatchComponent metallicTexture = batchBuilder.AddComponent(
                materialContainerSlotId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
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

        if (AddTextureComponentReference(
                batchBuilder,
                plannedMaterial,
                materialContainerSlotId,
                ResoniteSceneMaterialConventions.TextureMemberRole.Emission,
                "EmissiveMap",
                materialMembers))
        {
            materialMembers["EmissiveColor"] = ResoniteMaterialComponentPolicy.CreateColorMember(
                new ResoniteColor(1.0, 1.0, 1.0, 1.0));
        }

        return batchBuilder.AddComponent(
            materialContainerSlotId,
            ResoniteMaterialComponentPolicy.GetComponentType(plannedMaterial.Material),
            materialMembers);
    }

    private static bool AddTextureComponentReference(
        ResoniteBatchOperations.BatchActionBuilder batchBuilder,
        PlannedDedicatedMaterialAsset plannedMaterial,
        string materialContainerSlotId,
        ResoniteSceneMaterialConventions.TextureMemberRole textureRole,
        string materialMemberName,
        Dictionary<string, Member> materialMembers)
    {
        Uri? textureUri = TryGetPlannedTextureUri(plannedMaterial.Textures, textureRole);
        if (textureUri is null)
        {
            return false;
        }

        ResoniteBatchOperations.PendingBatchComponent textureComponent = batchBuilder.AddComponent(
            materialContainerSlotId,
            "[FrooxEngine]FrooxEngine.StaticTexture2D",
            ResoniteSceneMaterialConventions.CreateTextureMembers(textureUri, textureRole));
        materialMembers[materialMemberName] = new Reference
        {
            TargetID = textureComponent.LocalId.Value,
        };
        return true;
    }

    public static async Task<CreatedSlot?> TryGetExistingSharedChildSlotAsync(
        IResoniteLinkClient client,
        ResoniteSlotLocator parentSlot,
        string childSlotName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentSlot.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(childSlotName);

        Slot? parentSlotSnapshot = await client.GetSlotAsync(new ResoniteTransportSlotLocator(parentSlot.Value), 1, cancellationToken);
        ResoniteSceneChildLookupResult childLookup = new ResoniteSceneSlotSnapshot(parentSlotSnapshot)
            .GetUniqueChildLookupResult(childSlotName, parentSlot.Value);
        return childLookup.State == ResoniteSceneChildLookupState.FoundWithId
            ? new CreatedSlot(new ResoniteSlotLocator(childLookup.Slot!.ID!), childSlotName)
            : null;
    }

    public static async Task<CreatedComponent> CreateComponentAsync(
        IResoniteLinkClient client,
        ResoniteSlotLocator containerSlot,
        string componentType,
        IReadOnlyDictionary<string, Member> members,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerSlot.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentType);
        ArgumentNullException.ThrowIfNull(members);

        ResoniteBatchOperations.BatchActionBuilder batchBuilder = new();
        ResoniteBatchOperations.PendingBatchComponent pendingComponent = batchBuilder.AddComponent(
            containerSlot.Value,
            componentType,
            members);
        BatchResponse response = await client.RunDataModelOperationBatchAsync(batchBuilder.Actions, cancellationToken);
        return CanonicalBatchEntityMap.Create(response).ResolveComponent(pendingComponent);
    }

    public static ResoniteMaterialBinding ResolveTerrainTextureCanvasMaterial(
        ResoniteMaterialBinding material,
        IReadOnlyDictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(preparedTerrainTextureDataByOverlay);

        if (material.TerrainOverlay is null
            || !preparedTerrainTextureDataByOverlay.TryGetValue(material.TerrainOverlay, out GeneratedTerrainTexture? generatedTerrainTexture))
        {
            return material;
        }

        (ScalarPair? effectiveTextureScale, ScalarPair? effectiveTextureOffset)
            = TextureUvRect.ComposeMaterialTransformValue(
                generatedTerrainTexture.OccupiedUvRect,
                material.TextureScale is null ? null : new ScalarPair(material.TextureScale.X, material.TextureScale.Y),
                material.TextureOffset is null ? null : new ScalarPair(material.TextureOffset.X, material.TextureOffset.Y));
        return material with
        {
            TextureScale = effectiveTextureScale is null ? null : new ResoniteFloat2(effectiveTextureScale.X, effectiveTextureScale.Y),
            TextureOffset = effectiveTextureOffset is null ? null : new ResoniteFloat2(effectiveTextureOffset.X, effectiveTextureOffset.Y),
        };
    }

    private async Task<List<PlannedTextureAsset>> PlanBundledCompanionTexturesAsync(
        IResoniteLinkClient importClient,
        ResoniteMaterialBinding material,
        Task<Uri?> albedoTextureTask,
        AsyncInFlightResultCache<BundledTextureImportKey, Uri>? bundledTextureImportTasks,
        CancellationToken cancellationToken)
    {
        Task<Uri?> normalTextureTask = Task.FromResult<Uri?>(null);
        Task<Uri?> heightTextureTask = Task.FromResult<Uri?>(null);
        Task<Uri?> metallicTextureTask = Task.FromResult<Uri?>(null);
        Task<Uri?> emissionTextureTask = Task.FromResult<Uri?>(null);

        if (ResoniteMaterialComponentPolicy.TryGetBundledCompanionTextureSet(
                bundledDefaultMaterialAssetStore,
                material,
                out BundledDefaultMaterialTextureSet? textureSet)
            && textureSet is not null)
        {
            if (textureSet.Normal is not null)
            {
                normalTextureTask = ImportBundledTextureAsync(
                    importClient,
                    textureSet.Normal,
                    ResoniteTextureColorProfiles.Linear,
                    bundledTextureImportTasks,
                    cancellationToken);
            }

            if (textureSet.Height is not null
                && material.Projection == ResoniteMaterialProjection.Uv)
            {
                heightTextureTask = ImportBundledTextureAsync(
                    importClient,
                    textureSet.Height,
                    ResoniteTextureColorProfiles.Linear,
                    bundledTextureImportTasks,
                    cancellationToken);
            }

            if (textureSet.Metallic is not null)
            {
                metallicTextureTask = ImportBundledTextureAsync(
                    importClient,
                    textureSet.Metallic,
                    ResoniteTextureColorProfiles.Linear,
                    bundledTextureImportTasks,
                    cancellationToken);
            }

            if (textureSet.Emission is not null)
            {
                emissionTextureTask = ImportBundledTextureAsync(
                    importClient,
                    textureSet.Emission,
                    ResoniteTextureColorProfiles.Srgb,
                    bundledTextureImportTasks,
                    cancellationToken);
            }
        }

        await Task.WhenAll(
            albedoTextureTask,
            normalTextureTask,
            heightTextureTask,
            metallicTextureTask,
            emissionTextureTask);

        List<PlannedTextureAsset> textures = [];
        AddPlannedTextureAsset(
            textures,
            ResoniteSceneMaterialConventions.TextureMemberRole.Albedo,
            await albedoTextureTask);
        AddPlannedTextureAsset(
            textures,
            ResoniteSceneMaterialConventions.TextureMemberRole.Normal,
            await normalTextureTask);
        AddPlannedTextureAsset(
            textures,
            ResoniteSceneMaterialConventions.TextureMemberRole.Height,
            await heightTextureTask);
        AddPlannedTextureAsset(
            textures,
            ResoniteSceneMaterialConventions.TextureMemberRole.Metallic,
            await metallicTextureTask);
        AddPlannedTextureAsset(
            textures,
            ResoniteSceneMaterialConventions.TextureMemberRole.Emission,
            await emissionTextureTask);
        return textures;
    }

    private async Task<Uri?> ImportBundledTextureAsync(
        IResoniteLinkClient importClient,
        BundledDefaultTextureAsset asset,
        string colorProfile,
        AsyncInFlightResultCache<BundledTextureImportKey, Uri>? bundledTextureImportTasks,
        CancellationToken cancellationToken)
    {
        if (bundledTextureImportTasks is null)
        {
            return await ImportBundledTextureCoreAsync(importClient, asset, colorProfile, cancellationToken);
        }

        BundledTextureImportKey importKey = new(asset, colorProfile);
        return await bundledTextureImportTasks.GetOrCreateAsync(
            importKey,
            factoryCancellationToken => ImportBundledTextureCoreAsync(
                importClient,
                asset,
                colorProfile,
                factoryCancellationToken),
            factoryCancellationToken: cancellationToken,
            cancellationToken);
    }

    private async Task<Uri> ImportBundledTextureCoreAsync(
        IResoniteLinkClient importClient,
        BundledDefaultTextureAsset asset,
        string colorProfile,
        CancellationToken cancellationToken)
    {
        string absolutePath = bundledDefaultMaterialAssetStore.GetAbsolutePath(asset);
        ITextureImportSource textureSource = ResoniteTextureImportFactory.CreateSourceFromFile(
            absolutePath,
            colorProfile);
        return await importClient.ImportTextureAsync(textureSource, cancellationToken);
    }

    private Task<Uri?> ImportBundledAlbedoTextureAsync(
        IResoniteLinkClient importClient,
        ResoniteMaterialBinding material,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(material.Family))
        {
            return Task.FromResult<Uri?>(null);
        }

        return ImportBundledTextureAsync(
            importClient,
            GetBundledAlbedoTextureAsset(material),
            ResoniteTextureColorProfiles.Srgb,
            bundledTextureImportTasks: null,
            cancellationToken);
    }

    private static BundledDefaultTextureAsset<BundledDefaultAlbedoTextureRole> GetBundledAlbedoTextureAsset(ResoniteMaterialBinding material)
    {
        BundledDefaultMaterialVariant variant = BundledDefaultMaterialFamilies.GetVariantDefinition(
            material.Family!,
            material.BundledVariantIndex ?? 0);
        return variant.TextureSources?.Albedo ?? variant.Albedo;
    }

    private static void AddPlannedTextureAsset(
        List<PlannedTextureAsset> textures,
        ResoniteSceneMaterialConventions.TextureMemberRole role,
        Uri? assetUri)
    {
        if (assetUri is null)
        {
            return;
        }

        textures.Add(new PlannedTextureAsset(
            ResoniteSceneMaterialConventions.CreateTextureIdentity(role),
            assetUri));
    }

}
