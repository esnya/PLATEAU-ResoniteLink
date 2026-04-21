using System;
using System.Collections.Generic;
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
        IReadOnlyDictionary<string, ResoniteTextureImport> preparedTextureDataByIdentity,
        IReadOnlyDictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay,
        bool preserveDedicatedMaterialSlot,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteMaterialPlanning : IResoniteMaterialPlanning
{
    private const float DefaultNormalScale = 1.0f;
    private const float DefaultBundledHeightScale = 0.002f;

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
            string albedoPath = BundledDefaultMaterialAssetStore.GetAbsolutePath(
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
        IReadOnlyDictionary<string, ResoniteTextureImport> preparedTextureDataByIdentity,
        IReadOnlyDictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay,
        bool preserveDedicatedMaterialSlot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(importClient);
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(preparedTextureDataByIdentity);
        ArgumentNullException.ThrowIfNull(preparedTerrainTextureDataByOverlay);

        Task<Uri?> albedoTextureTask = material.TexturePayload is not null
            && !string.IsNullOrWhiteSpace(material.TexturePayload.Identity)
            && preparedTextureDataByIdentity.TryGetValue(
                material.TexturePayload.Identity,
                out ResoniteTextureImport? directTextureImport)
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
        IReadOnlyDictionary<string, ResoniteTextureImport> preparedTextureDataByIdentity,
        IReadOnlyDictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(importClient);
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(preparedTextureDataByIdentity);
        ArgumentNullException.ThrowIfNull(preparedTerrainTextureDataByOverlay);

        ResoniteTextureImport? textureImport = material.TexturePayload is not null
            && !string.IsNullOrWhiteSpace(material.TexturePayload.Identity)
            && preparedTextureDataByIdentity.TryGetValue(material.TexturePayload.Identity, out ResoniteTextureImport? directTextureImport)
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
            : new PlannedTextureAsset(new TextureIdentity($"main-texture-override:{textureIdentity}"), textureUri);
    }

    public static async Task<CreatedMaterialAsset> EmitCommonMaterialAsync(
        IResoniteLinkClient client,
        PlannedDedicatedMaterialAsset plannedMaterial,
        string commonAssetsSlotId,
        string materialSlotName,
        Func<IResoniteLinkClient, string, string, CancellationToken, Task<CreatedSlot>> getOrCreateSharedChildSlotAsync,
        Func<IResoniteLinkClient, string, string, IReadOnlyDictionary<string, Member>, CancellationToken, Task<CreatedComponent>> createComponentAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(plannedMaterial);
        ArgumentException.ThrowIfNullOrWhiteSpace(commonAssetsSlotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialSlotName);
        ArgumentNullException.ThrowIfNull(getOrCreateSharedChildSlotAsync);
        ArgumentNullException.ThrowIfNull(createComponentAsync);

        CreatedSlot materialSlot = await getOrCreateSharedChildSlotAsync(
            client,
            commonAssetsSlotId,
            materialSlotName,
            cancellationToken);
        string materialContainerSlotId = materialSlot.SlotId;
        Dictionary<string, Member> materialMembers = ResoniteMaterialComponentPolicy.CreateMembers(plannedMaterial.Material);

        Uri? albedoTextureUri = TryGetPlannedTextureUri(plannedMaterial.Textures, "albedo");
        if (albedoTextureUri is not null)
        {
            CreatedComponent albedoTexture = await createComponentAsync(
                client,
                materialContainerSlotId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ResoniteSceneMaterialConventions.CreateTextureMembers(
                    albedoTextureUri,
                    ResoniteSceneMaterialConventions.TextureMemberRole.Albedo),
                cancellationToken);
            materialMembers["AlbedoTexture"] = new Reference
            {
                TargetID = albedoTexture.ComponentId,
            };
        }

        Uri? normalTextureUri = TryGetPlannedTextureUri(plannedMaterial.Textures, "normal");
        if (normalTextureUri is not null)
        {
            CreatedComponent normalTexture = await createComponentAsync(
                client,
                materialContainerSlotId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ResoniteSceneMaterialConventions.CreateTextureMembers(
                    normalTextureUri,
                    ResoniteSceneMaterialConventions.TextureMemberRole.Normal),
                cancellationToken);
            materialMembers["NormalMap"] = new Reference
            {
                TargetID = normalTexture.ComponentId,
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
                materialContainerSlotId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ResoniteSceneMaterialConventions.CreateTextureMembers(
                    heightTextureUri,
                    ResoniteSceneMaterialConventions.TextureMemberRole.Height),
                cancellationToken);
            materialMembers["HeightMap"] = new Reference
            {
                TargetID = heightTexture.ComponentId,
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
                materialContainerSlotId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ResoniteSceneMaterialConventions.CreateTextureMembers(
                    metallicTextureUri,
                    ResoniteSceneMaterialConventions.TextureMemberRole.Metallic),
                cancellationToken);
            materialMembers["MetallicMap"] = new Reference
            {
                TargetID = metallicTexture.ComponentId,
            };
            materialMembers["OcclusionMap"] = new Reference
            {
                TargetID = metallicTexture.ComponentId,
            };
        }

        Uri? emissionTextureUri = TryGetPlannedTextureUri(plannedMaterial.Textures, "emission");
        if (emissionTextureUri is not null)
        {
            CreatedComponent emissionTexture = await createComponentAsync(
                client,
                materialContainerSlotId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ResoniteSceneMaterialConventions.CreateTextureMembers(
                    emissionTextureUri,
                    ResoniteSceneMaterialConventions.TextureMemberRole.Emission),
                cancellationToken);
            materialMembers["EmissiveMap"] = new Reference
            {
                TargetID = emissionTexture.ComponentId,
            };
            materialMembers["EmissiveColor"] = ResoniteMaterialComponentPolicy.CreateColorMember(
                new ResoniteColor(1.0, 1.0, 1.0, 1.0));
        }

        CreatedComponent materialComponent = await createComponentAsync(
            client,
            materialContainerSlotId,
            ResoniteMaterialComponentPolicy.GetComponentType(plannedMaterial.Material),
            materialMembers,
            cancellationToken);
        return new CreatedMaterialAsset(materialComponent.ComponentId, null);
    }

    public static async Task<CreatedSlot?> TryGetExistingSharedChildSlotAsync(
        IResoniteLinkClient client,
        string parentSlotId,
        string childSlotName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentSlotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(childSlotName);

        Slot? parentSlotSnapshot = await client.GetSlotAsync(parentSlotId, 1, cancellationToken);
        ResoniteSceneChildLookupResult childLookup = new ResoniteSceneSlotSnapshot(parentSlotSnapshot)
            .GetUniqueChildLookupResult(childSlotName, parentSlotId);
        return childLookup.State == ResoniteSceneChildLookupState.FoundWithId
            ? new CreatedSlot(childLookup.SlotId!, childSlotName)
            : null;
    }

    public static async Task<string?> TryGetExistingCommonMaterialComponentIdAsync(
        IResoniteLinkClient client,
        string familySlotId,
        IReadOnlyList<string> materialSlotNames,
        string materialComponentType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(familySlotId);
        ArgumentNullException.ThrowIfNull(materialSlotNames);
        if (materialSlotNames.Count == 0 || materialSlotNames.All(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one material slot lookup name is required.", nameof(materialSlotNames));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(materialComponentType);

        Slot? familySlotSnapshot = await client.GetSlotAsync(familySlotId, 1, cancellationToken);
        if (familySlotSnapshot is null)
        {
            return null;
        }

        ResoniteSceneSlotSnapshot familySlot = new(familySlotSnapshot);
        foreach (string materialSlotName in materialSlotNames.Where(static name => !string.IsNullOrWhiteSpace(name)))
        {
            ResoniteSceneChildLookupResult materialLookup = familySlot.GetUniqueChildLookupResult(materialSlotName, familySlotId);
            string? existingMaterialComponentId = materialLookup.Slot?.Components?
                .Where(component => string.Equals(component.ComponentType, materialComponentType, StringComparison.Ordinal))
                .OrderBy(static component => component.ID, StringComparer.Ordinal)
                .Select(static component => component.ID)
                .FirstOrDefault(static id => !string.IsNullOrWhiteSpace(id));
            if (!string.IsNullOrWhiteSpace(existingMaterialComponentId))
            {
                return existingMaterialComponentId;
            }
        }

        return null;
    }

    public static async Task<CreatedComponent> CreateComponentAsync(
        IResoniteLinkClient client,
        string containerSlotId,
        string componentType,
        IReadOnlyDictionary<string, Member> members,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerSlotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentType);
        ArgumentNullException.ThrowIfNull(members);

        ResoniteBatchOperations.PendingBatchComponent pendingComponent = new(
            LocalId: $"single_component_{Guid.NewGuid():N}",
            MessageId: $"single_component_message_{Guid.NewGuid():N}",
            ComponentType: componentType);
        BatchResponse response = await client.RunDataModelOperationBatchAsync(
            [ResoniteBatchOperations.CreateAddComponentOperation(containerSlotId, componentType, members, pendingComponent.LocalId, pendingComponent.MessageId)],
            cancellationToken);
        return CanonicalBatchEntityMap.Create(response).ResolveComponent(pendingComponent);
    }

    public static MaterialIdentity CreateDedicatedMaterialIdentity(
        string packageName,
        int materialIndex,
        string materialKey)
    {
        return new MaterialIdentity(
            string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"dedicated|{packageName}|{materialIndex}|{materialKey}"));
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

        (ResoniteFloat2? effectiveTextureScale, ResoniteFloat2? effectiveTextureOffset)
            = TextureUvRect.ComposeMaterialTransform(
                generatedTerrainTexture.OccupiedUvRect,
                material.TextureScale,
                material.TextureOffset);
        return material with
        {
            TextureScale = effectiveTextureScale,
            TextureOffset = effectiveTextureOffset,
        };
    }

    private static async Task<List<PlannedTextureAsset>> PlanBundledCompanionTexturesAsync(
        IResoniteLinkClient importClient,
        ResoniteMaterialBinding material,
        Task<Uri?> albedoTextureTask,
        CancellationToken cancellationToken)
    {
        Task<Uri?> normalTextureTask = Task.FromResult<Uri?>(null);
        Task<Uri?> heightTextureTask = Task.FromResult<Uri?>(null);
        Task<Uri?> metallicTextureTask = Task.FromResult<Uri?>(null);
        Task<Uri?> emissionTextureTask = Task.FromResult<Uri?>(null);

        if (ResoniteMaterialComponentPolicy.TryGetBundledCompanionTextureSet(material, out BundledDefaultMaterialTextureSet? textureSet)
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

    private static Task<Uri?> ImportBundledAlbedoTextureAsync(
        IResoniteLinkClient importClient,
        ResoniteMaterialBinding material,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(material.Family))
        {
            return Task.FromResult<Uri?>(null);
        }

        string albedoPath = BundledDefaultMaterialAssetStore.GetAbsolutePath(
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
