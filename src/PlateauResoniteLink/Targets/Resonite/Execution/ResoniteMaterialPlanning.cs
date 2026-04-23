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
        CancellationToken cancellationToken);

    Task<PlannedDedicatedMaterialAsset> PlanDedicatedMaterialAssetAsync(
        IResoniteLinkClient importClient,
        ResoniteMaterialBinding material,
        int materialIndex,
        string packageName,
        IReadOnlyDictionary<ResoniteTexturePayload, ResoniteTextureImport> preparedTextureDataByPayload,
        IReadOnlyDictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay,
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
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(importClient);
        ArgumentNullException.ThrowIfNull(material);

        Task<Uri?> albedoTextureTask = Task.FromResult<Uri?>(null);
        if (!string.IsNullOrWhiteSpace(material.Family))
        {
            string albedoPath = bundledDefaultMaterialAssetStore.GetAbsolutePath(
                BundledDefaultMaterialFamilies.GetVariant(material.Family!, material.BundledVariantIndex ?? 0));
            ResoniteRawTextureImport albedoTexture = await ResoniteTextureImportFactory.CreateRawFromFileAsync(
                albedoPath,
                ResoniteTextureColorProfiles.Srgb,
                cancellationToken);
            albedoTextureTask = ImportOptionalTextureAsync(importClient, albedoTexture, cancellationToken);
        }

        List<PlannedTextureAsset> textures = await PlanBundledCompanionTexturesAsync(
            importClient,
            material,
            albedoTextureTask,
            cancellationToken);
        return new PlannedDedicatedMaterialAsset(
            new MaterialIdentity(material.MaterialKey),
            material,
            textures,
            PreserveDedicatedMaterialSlot: false);
    }

    public async Task<PlannedDedicatedMaterialAsset> PlanDedicatedMaterialAssetAsync(
        IResoniteLinkClient importClient,
        ResoniteMaterialBinding material,
        int materialIndex,
        string packageName,
        IReadOnlyDictionary<ResoniteTexturePayload, ResoniteTextureImport> preparedTextureDataByPayload,
        IReadOnlyDictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay,
        bool preserveDedicatedMaterialSlot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(importClient);
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(preparedTextureDataByPayload);
        ArgumentNullException.ThrowIfNull(preparedTerrainTextureDataByOverlay);

        Task<Uri?> albedoTextureTask = material.TexturePayload is not null
            && preparedTextureDataByPayload.TryGetValue(material.TexturePayload, out ResoniteTextureImport? directTextureImport)
            ? ImportOptionalTextureAsync(importClient, directTextureImport, cancellationToken)
            : material.TerrainOverlay is not null
            && preparedTerrainTextureDataByOverlay.TryGetValue(
                material.TerrainOverlay,
                out GeneratedTerrainTexture? terrainOverlayTexture)
            ? ImportOptionalTextureAsync(importClient, terrainOverlayTexture.TextureImport, cancellationToken)
            : !string.IsNullOrWhiteSpace(material.Family)
            ? ImportBundledAlbedoTextureAsync(importClient, material, cancellationToken)
            : Task.FromResult<Uri?>(null);

        List<PlannedTextureAsset> textures = await PlanBundledCompanionTexturesAsync(
            importClient,
            material,
            albedoTextureTask,
            cancellationToken);
        return new PlannedDedicatedMaterialAsset(
            CreateDedicatedMaterialIdentity(packageName, materialIndex, material.MaterialKey),
            material,
            textures,
            preserveDedicatedMaterialSlot);
    }

    public static Uri? TryGetPlannedTextureUri(
        IEnumerable<PlannedTextureAsset> textures,
        string role)
    {
        ArgumentNullException.ThrowIfNull(textures);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        return textures.FirstOrDefault(texture => string.Equals(texture.Identity.Value, role, StringComparison.Ordinal))?.AssetUri;
    }

    public static async Task<PlannedTextureAsset?> PlanMainTextureOverrideAsync(
        IResoniteLinkClient importClient,
        ResoniteMaterialBinding material,
        IReadOnlyDictionary<ResoniteTexturePayload, ResoniteTextureImport> preparedTextureDataByPayload,
        IReadOnlyDictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(importClient);
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(preparedTextureDataByPayload);
        ArgumentNullException.ThrowIfNull(preparedTerrainTextureDataByOverlay);

        ResoniteTextureImport? textureImport = material.TexturePayload is not null
            && preparedTextureDataByPayload.TryGetValue(material.TexturePayload, out ResoniteTextureImport? directTextureImport)
                ? directTextureImport
            : material.TerrainOverlay is not null
            && preparedTerrainTextureDataByOverlay.TryGetValue(material.TerrainOverlay, out GeneratedTerrainTexture? terrainOverlayTextureImport)
                ? terrainOverlayTextureImport.TextureImport
            : null;
        if (textureImport is null)
        {
            return null;
        }

        Uri? textureUri = await ImportOptionalTextureAsync(importClient, textureImport, cancellationToken);
        string textureIdentity = textureImport switch
        {
            ResoniteRawTextureImport rawTexture when !string.IsNullOrWhiteSpace(rawTexture.Identity) => rawTexture.Identity!,
            _ => material.MaterialKey,
        };
        return textureUri is null
            ? null
            : new PlannedTextureAsset(
                new TextureIdentity(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"main-tex-override-{material.MaterialKey}-{textureIdentity}")),
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

        Uri? albedoTextureUri = TryGetPlannedTextureUri(plannedMaterial.Textures, "albedo");
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

        Uri? normalTextureUri = TryGetPlannedTextureUri(plannedMaterial.Textures, "normal");
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

        Uri? heightTextureUri = TryGetPlannedTextureUri(plannedMaterial.Textures, "height");
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

        Uri? metallicTextureUri = TryGetPlannedTextureUri(plannedMaterial.Textures, "metallic");
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

        Uri? emissionTextureUri = TryGetPlannedTextureUri(plannedMaterial.Textures, "emission");
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

    public static MaterialIdentity CreateDedicatedMaterialIdentity(
        string packageName,
        int materialIndex,
        string materialKey)
    {
        return new MaterialIdentity(
            string.Create(
                CultureInfo.InvariantCulture,
                $"dedicated-{packageName.ToLowerInvariant()}-{materialIndex}-{materialKey}"));
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
                    cancellationToken);
            }

            if (textureSet.HeightPath is not null
                && material.Projection == ResoniteMaterialProjection.Uv)
            {
                heightTextureTask = ImportTextureFromFileAsync(
                    importClient,
                    textureSet.HeightPath,
                    ResoniteTextureColorProfiles.Linear,
                    cancellationToken);
            }

            if (textureSet.MetallicPath is not null)
            {
                metallicTextureTask = ImportTextureFromFileAsync(
                    importClient,
                    textureSet.MetallicPath,
                    ResoniteTextureColorProfiles.Linear,
                    cancellationToken);
            }

            if (textureSet.EmissionPath is not null)
            {
                emissionTextureTask = ImportTextureFromFileAsync(
                    importClient,
                    textureSet.EmissionPath,
                    ResoniteTextureColorProfiles.Srgb,
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
        AddPlannedTextureAsset(textures, "albedo", await albedoTextureTask);
        AddPlannedTextureAsset(textures, "normal", await normalTextureTask);
        AddPlannedTextureAsset(textures, "height", await heightTextureTask);
        AddPlannedTextureAsset(textures, "metallic", await metallicTextureTask);
        AddPlannedTextureAsset(textures, "emission", await emissionTextureTask);
        return textures;
    }

    private static async Task<Uri?> ImportTextureFromFileAsync(
        IResoniteLinkClient importClient,
        string absolutePath,
        string colorProfile,
        CancellationToken cancellationToken)
    {
        ResoniteRawTextureImport textureImport = await ResoniteTextureImportFactory.CreateRawFromFileAsync(
            absolutePath,
            colorProfile,
            cancellationToken);
        return await ImportOptionalTextureAsync(importClient, textureImport, cancellationToken);
    }

    private static async Task<Uri?> ImportOptionalTextureAsync(
        IResoniteLinkClient importClient,
        ResoniteTextureImport textureImport,
        CancellationToken cancellationToken)
    {
        return await importClient.ImportTextureAsync(textureImport, cancellationToken);
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
            cancellationToken);
    }

    private static void AddPlannedTextureAsset(
        List<PlannedTextureAsset> textures,
        string role,
        Uri? assetUri)
    {
        if (assetUri is null)
        {
            return;
        }

        textures.Add(new PlannedTextureAsset(new TextureIdentity(role), assetUri));
    }

}
