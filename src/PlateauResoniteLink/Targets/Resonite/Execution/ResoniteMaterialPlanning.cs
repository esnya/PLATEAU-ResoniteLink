using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite.Execution;

internal interface IResoniteMaterialPlanning
{
    Task<PlannedDedicatedMaterialAsset> PlanCommonMaterialAssetAsync(
        IResoniteLinkClient importClient,
        ResoniteMaterialBinding material,
        AsyncInFlightResultCache<string, Uri> bundledTextureImportTasks,
        CancellationToken cancellationToken);

    Task<PlannedDedicatedMaterialAsset> PlanDedicatedMaterialAssetAsync(
        IResoniteLinkClient importClient,
        ResoniteMaterialBinding material,
        int materialIndex,
        string packageName,
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
        AsyncInFlightResultCache<string, Uri> bundledTextureImportTasks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(importClient);
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(bundledTextureImportTasks);

        Task<Uri?> albedoTextureTask = Task.FromResult<Uri?>(null);
        if (!string.IsNullOrWhiteSpace(material.Family))
        {
            string albedoPath = bundledDefaultMaterialAssetStore.GetAbsolutePath(
                BundledDefaultMaterialFamilies.GetVariant(material.Family!, material.BundledVariantIndex ?? 0));
            ResoniteRawTextureImport albedoTexture = await ResoniteTextureImportFactory.CreateRawFromFileAsync(
                albedoPath,
                ResoniteTextureColorProfiles.Srgb,
                cancellationToken);
            albedoTextureTask = ImportOptionalTextureAsync(
                importClient,
                albedoTexture,
                CreateBundledTextureImportKey(albedoPath, ResoniteTextureColorProfiles.Srgb),
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
        string packageName,
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
            preserveDedicatedMaterialSlot);
    }

    public static Uri? TryGetPlannedTextureUri(
        IEnumerable<PlannedTextureAsset> textures,
        ResoniteSceneMaterialConventions.TextureMemberRole role)
    {
        ArgumentNullException.ThrowIfNull(textures);

        TextureIdentity identity = ResoniteSceneMaterialConventions.CreateTextureIdentity(role);
        return textures.FirstOrDefault(texture => texture.Identity == identity)?.AssetUri;
    }

    public static async Task<PlannedTextureAsset?> PlanMainTextureOverrideAsync(
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

        await Task.CompletedTask;
        return new PlannedTextureAsset(
            new TextureIdentity("main"),
            textureUri);
    }

    public static async Task<CreatedMaterialAsset> EmitCommonMaterialAsync(
        IResoniteLinkClient client,
        PlannedDedicatedMaterialAsset plannedMaterial,
        ResoniteSlotLocator commonAssetsSlot,
        string materialSlotName,
        Func<IResoniteLinkClient, ResoniteSlotLocator, string, CancellationToken, Task<CreatedSlot>> getOrCreateSharedChildSlotAsync,
        Func<IResoniteLinkClient, ResoniteSlotLocator, string, IReadOnlyDictionary<string, Member>, CancellationToken, Task<CreatedComponent>> createComponentAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(plannedMaterial);
        ArgumentException.ThrowIfNullOrWhiteSpace(commonAssetsSlot.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialSlotName);
        ArgumentNullException.ThrowIfNull(getOrCreateSharedChildSlotAsync);
        ArgumentNullException.ThrowIfNull(createComponentAsync);

        CreatedSlot materialSlot = await getOrCreateSharedChildSlotAsync(
            client,
            commonAssetsSlot,
            materialSlotName,
            cancellationToken);
        ResoniteSlotLocator materialContainerSlot = materialSlot.Locator;
        Dictionary<string, Member> materialMembers = ResoniteMaterialComponentPolicy.CreateMembers(plannedMaterial.Material);

        Uri? albedoTextureUri = TryGetPlannedTextureUri(
            plannedMaterial.Textures,
            ResoniteSceneMaterialConventions.TextureMemberRole.Albedo);
        if (albedoTextureUri is not null)
        {
            CreatedComponent albedoTexture = await createComponentAsync(
                client,
                materialContainerSlot,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ResoniteSceneMaterialConventions.CreateTextureMembers(
                    albedoTextureUri,
                    ResoniteSceneMaterialConventions.TextureMemberRole.Albedo),
                cancellationToken);
            materialMembers["AlbedoTexture"] = new Reference
            {
                TargetID = albedoTexture.Locator.Value,
            };
        }

        Uri? normalTextureUri = TryGetPlannedTextureUri(
            plannedMaterial.Textures,
            ResoniteSceneMaterialConventions.TextureMemberRole.Normal);
        if (normalTextureUri is not null)
        {
            CreatedComponent normalTexture = await createComponentAsync(
                client,
                materialContainerSlot,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ResoniteSceneMaterialConventions.CreateTextureMembers(
                    normalTextureUri,
                    ResoniteSceneMaterialConventions.TextureMemberRole.Normal),
                cancellationToken);
            materialMembers["NormalMap"] = new Reference
            {
                TargetID = normalTexture.Locator.Value,
            };
            materialMembers["NormalScale"] = new Field_float
            {
                Value = DefaultNormalScale,
            };
        }

        Uri? heightTextureUri = TryGetPlannedTextureUri(
            plannedMaterial.Textures,
            ResoniteSceneMaterialConventions.TextureMemberRole.Height);
        if (heightTextureUri is not null)
        {
            CreatedComponent heightTexture = await createComponentAsync(
                client,
                materialContainerSlot,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ResoniteSceneMaterialConventions.CreateTextureMembers(
                    heightTextureUri,
                    ResoniteSceneMaterialConventions.TextureMemberRole.Height),
                cancellationToken);
            materialMembers["HeightMap"] = new Reference
            {
                TargetID = heightTexture.Locator.Value,
            };
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
            CreatedComponent metallicTexture = await createComponentAsync(
                client,
                materialContainerSlot,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ResoniteSceneMaterialConventions.CreateTextureMembers(
                    metallicTextureUri,
                    ResoniteSceneMaterialConventions.TextureMemberRole.Metallic),
                cancellationToken);
            materialMembers["MetallicMap"] = new Reference
            {
                TargetID = metallicTexture.Locator.Value,
            };
            materialMembers["OcclusionMap"] = new Reference
            {
                TargetID = metallicTexture.Locator.Value,
            };
        }

        Uri? emissionTextureUri = TryGetPlannedTextureUri(
            plannedMaterial.Textures,
            ResoniteSceneMaterialConventions.TextureMemberRole.Emission);
        if (emissionTextureUri is not null)
        {
            CreatedComponent emissionTexture = await createComponentAsync(
                client,
                materialContainerSlot,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ResoniteSceneMaterialConventions.CreateTextureMembers(
                    emissionTextureUri,
                    ResoniteSceneMaterialConventions.TextureMemberRole.Emission),
                cancellationToken);
            materialMembers["EmissiveMap"] = new Reference
            {
                TargetID = emissionTexture.Locator.Value,
            };
            materialMembers["EmissiveColor"] = ResoniteMaterialComponentPolicy.CreateColorMember(
                new ResoniteColor(1.0, 1.0, 1.0, 1.0));
        }

        CreatedComponent materialComponent = await createComponentAsync(
            client,
            materialContainerSlot,
            ResoniteMaterialComponentPolicy.GetComponentType(plannedMaterial.Material),
            materialMembers,
            cancellationToken);
        return new CreatedMaterialAsset(materialComponent.Locator, null);
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
        AsyncInFlightResultCache<string, Uri>? bundledTextureImportTasks,
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
            if (textureSet.NormalPath is not null)
            {
                normalTextureTask = ImportTextureFromFileAsync(
                    importClient,
                    textureSet.NormalPath,
                    ResoniteTextureColorProfiles.Linear,
                    bundledTextureImportTasks,
                    cancellationToken);
            }

            if (textureSet.HeightPath is not null
                && material.Projection == ResoniteMaterialProjection.Uv)
            {
                heightTextureTask = ImportTextureFromFileAsync(
                    importClient,
                    textureSet.HeightPath,
                    ResoniteTextureColorProfiles.Linear,
                    bundledTextureImportTasks,
                    cancellationToken);
            }

            if (textureSet.MetallicPath is not null)
            {
                metallicTextureTask = ImportTextureFromFileAsync(
                    importClient,
                    textureSet.MetallicPath,
                    ResoniteTextureColorProfiles.Linear,
                    bundledTextureImportTasks,
                    cancellationToken);
            }

            if (textureSet.EmissionPath is not null)
            {
                emissionTextureTask = ImportTextureFromFileAsync(
                    importClient,
                    textureSet.EmissionPath,
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

    private static async Task<Uri?> ImportTextureFromFileAsync(
        IResoniteLinkClient importClient,
        string absolutePath,
        string colorProfile,
        AsyncInFlightResultCache<string, Uri>? bundledTextureImportTasks,
        CancellationToken cancellationToken)
    {
        ResoniteRawTextureImport textureImport = await ResoniteTextureImportFactory.CreateRawFromFileAsync(
            absolutePath,
            colorProfile,
            cancellationToken);
        return await ImportOptionalTextureAsync(
            importClient,
            textureImport,
            bundledTextureImportTasks is null ? null : CreateBundledTextureImportKey(absolutePath, colorProfile),
            bundledTextureImportTasks,
            cancellationToken);
    }

    private static async Task<Uri?> ImportOptionalTextureAsync(
        IResoniteLinkClient importClient,
        ResoniteTextureImport textureImport,
        string? bundledTextureImportKey,
        AsyncInFlightResultCache<string, Uri>? bundledTextureImportTasks,
        CancellationToken cancellationToken)
    {
        if (bundledTextureImportTasks is null || string.IsNullOrWhiteSpace(bundledTextureImportKey))
        {
            return await importClient.ImportTextureAsync(textureImport, cancellationToken);
        }

        return await bundledTextureImportTasks.GetOrCreateAsync(
            bundledTextureImportKey,
            () => importClient.ImportTextureAsync(textureImport, cancellationToken),
            cancellationToken);
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

        string albedoPath = bundledDefaultMaterialAssetStore.GetAbsolutePath(
            BundledDefaultMaterialFamilies.GetVariant(material.Family!, material.BundledVariantIndex ?? 0));
        return ImportTextureFromFileAsync(
            importClient,
            albedoPath,
            ResoniteTextureColorProfiles.Srgb,
            bundledTextureImportTasks: null,
            cancellationToken);
    }

    private static string CreateBundledTextureImportKey(string absolutePath, string colorProfile)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{colorProfile}|{System.IO.Path.GetFullPath(absolutePath)}");
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
