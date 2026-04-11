using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Cli;

internal sealed class ResoniteMaterialAssetManager(
    Func<IResoniteLinkClient, string, string, Func<CancellationToken, Task<Uri>>, CancellationToken, Task<ResoniteLinkSceneBuilder.CreatedComponent>> createSharedAssetComponentAsync,
    Func<IResoniteLinkClient, string, string, Func<CancellationToken, Task<Uri>>, CancellationToken, Task<ResoniteLinkSceneBuilder.CreatedComponent>> createDedicatedAssetComponentAsync,
    Func<IResoniteLinkClient, string, string, CancellationToken, Task<ResoniteLinkSceneBuilder.CreatedSlot>> getOrCreateSharedChildSlotAsync,
    Func<IResoniteLinkClient, string, string, IReadOnlyDictionary<string, Member>, CancellationToken, Task<ResoniteLinkSceneBuilder.CreatedComponent>> createComponentAsync,
    Func<IResoniteLinkClient, string, int, CancellationToken, Task<Slot?>> getSlotAsync,
    Func<IResoniteLinkClient, ResoniteTextureImport, CancellationToken, Task<Uri>> importTextureAsync,
    Action<string>? progressReporter = null)
{
    private const float DefaultNormalScale = 1.0f;
    private const float DefaultBundledHeightScale = 0.002f;
    private readonly AsyncCompletedResultCache<(string ScopeSlotId, string MaterialSlotName), ResoniteLinkSceneBuilder.CreatedComponent> materialComponentCache = new();

    public async Task<CreatedMaterialAsset> CreateMaterialComponentAsync(
        IResoniteLinkClient client,
        ResoniteMaterialBinding material,
        IReadOnlyDictionary<TextureReferenceKey, ResoniteTextureImport> preparedTexturePathsByKey,
        string materialSlotId,
        string? materialSlotParentId,
        string materialSlotName,
        string rendererSlotId,
        string textureOverrideAssetSlotId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(preparedTexturePathsByKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialSlotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialSlotName);
        ArgumentException.ThrowIfNullOrWhiteSpace(rendererSlotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(textureOverrideAssetSlotId);
        ReportProgress($"[live] Material '{material.MaterialKey}' queued.");

        ResoniteLinkSceneBuilder.CreatedComponent materialComponent = await GetOrCreateMaterialComponentAsync(
            client,
            material,
            preparedTexturePathsByKey,
            materialSlotId,
            materialSlotParentId,
            materialSlotName,
            cancellationToken: cancellationToken);
        return new CreatedMaterialAsset(materialComponent.ComponentId, null);
    }

    private async Task<ResoniteLinkSceneBuilder.CreatedComponent> GetOrCreateMaterialComponentAsync(
        IResoniteLinkClient client,
        ResoniteMaterialBinding material,
        IReadOnlyDictionary<TextureReferenceKey, ResoniteTextureImport> preparedTexturePathsByKey,
        string materialSlotId,
        string? materialSlotParentId,
        string materialSlotName,
        bool suppressAlbedoTexture = false,
        CancellationToken cancellationToken = default)
    {
        (string ScopeSlotId, string MaterialSlotName) materialTaskKey = (materialSlotId, materialSlotName);
        return await materialComponentCache.GetOrCreateAsync(
            materialTaskKey,
            ct => CreateMaterialComponentCoreAsync(
                client,
                material,
                preparedTexturePathsByKey,
                materialSlotId,
                materialSlotParentId,
                materialSlotName,
                suppressAlbedoTexture,
                ct),
            cancellationToken);
    }

    private async Task<ResoniteLinkSceneBuilder.CreatedComponent> CreateMaterialComponentCoreAsync(
        IResoniteLinkClient client,
        ResoniteMaterialBinding material,
        IReadOnlyDictionary<TextureReferenceKey, ResoniteTextureImport> preparedTexturePathsByKey,
        string materialSlotId,
        string? materialSlotParentId,
        string materialSlotName,
        bool suppressAlbedoTexture,
        CancellationToken cancellationToken)
    {
        string materialContainerSlotId = materialSlotId;

        Func<string, string, Func<CancellationToken, Task<Uri>>, CancellationToken, Task<ResoniteLinkSceneBuilder.CreatedComponent>> createAssetComponentAsync =
            materialSlotParentId is null
                ? (containerSlotId, componentType, importAssetAsync, ct) => createDedicatedAssetComponentAsync(
                    client,
                    containerSlotId,
                    componentType,
                    importAssetAsync,
                    ct)
                : (containerSlotId, componentType, importAssetAsync, ct) => createSharedAssetComponentAsync(
                    client,
                    containerSlotId,
                    componentType,
                    importAssetAsync,
                    ct);
        Func<string, Func<CancellationToken, Task<Uri>>, CancellationToken, Task<ResoniteLinkSceneBuilder.CreatedComponent>> createTextureComponentAsync =
            (componentType, importAssetAsync, ct) => createAssetComponentAsync(
                materialContainerSlotId,
                componentType,
                importAssetAsync,
                ct);

        Dictionary<string, Member> materialMembers = ResoniteMaterialComponentBuilder.CreateMembers(material);
        string materialComponentType = ResoniteMaterialComponentBuilder.GetComponentType(material);
        ReportProgress(
            $"[live] Material '{material.MaterialKey}' resolving as '{materialComponentType}' "
            + $"(projection={material.Projection}, texture={material.TexturePath ?? "none"}).");

        Uri? albedoTextureUri = null;
        Uri? normalTextureUri = null;
        Uri? heightTextureUri = null;
        Uri? metallicTextureUri = null;
        Uri? emissionTextureUri = null;

        if (!suppressAlbedoTexture
            && material.TexturePath is not null
            && preparedTexturePathsByKey.TryGetValue(
                CreateTextureReferenceKey(material.TexturePath, material.TextureSourceKind),
                out ResoniteTextureImport? textureAsset))
        {
            ReportProgress($"[live] Material '{material.MaterialKey}' importing albedo texture.");
            albedoTextureUri = await importTextureAsync(client, textureAsset, cancellationToken);
        }

        if (ResoniteMaterialComponentBuilder.TryGetBundledCompanionTextureSet(material, out BundledDefaultMaterialTextureSet? textureSet)
            && textureSet is not null)
        {
            if (textureSet.NormalPath is not null)
            {
                ReportProgress(
                    $"[live] Material '{material.MaterialKey}' importing bundled normal map from '{textureSet.NormalPath}'.");
                normalTextureUri = await importTextureAsync(
                    client,
                    await ResoniteTextureImportFactory.CreateRawFromFileAsync(
                        textureSet.NormalPath,
                        cancellationToken: cancellationToken),
                    cancellationToken);
                materialMembers["NormalScale"] = new Field_float
                {
                    Value = DefaultNormalScale,
                };
            }

            if (textureSet.HeightPath is not null
                && material.Projection == ResoniteMaterialProjection.Uv)
            {
                ReportProgress(
                    $"[live] Material '{material.MaterialKey}' importing bundled height map from '{textureSet.HeightPath}'.");
                heightTextureUri = await importTextureAsync(
                    client,
                    await ResoniteTextureImportFactory.CreateRawFromFileAsync(
                        textureSet.HeightPath,
                        cancellationToken: cancellationToken),
                    cancellationToken);
                materialMembers["HeightScale"] = new Field_float
                {
                    Value = DefaultBundledHeightScale,
                };
            }

            if (textureSet.MetallicPath is not null)
            {
                ReportProgress(
                    $"[live] Material '{material.MaterialKey}' importing bundled metallic map from '{textureSet.MetallicPath}'.");
                metallicTextureUri = await importTextureAsync(
                    client,
                    await ResoniteTextureImportFactory.CreateRawFromFileAsync(
                        textureSet.MetallicPath,
                        cancellationToken: cancellationToken),
                    cancellationToken);
            }

            if (textureSet.EmissionPath is not null)
            {
                ReportProgress(
                    $"[live] Material '{material.MaterialKey}' importing bundled emission map from '{textureSet.EmissionPath}'.");
                emissionTextureUri = await importTextureAsync(
                    client,
                    await ResoniteTextureImportFactory.CreateRawFromFileAsync(
                        textureSet.EmissionPath,
                        cancellationToken: cancellationToken),
                    cancellationToken);
                materialMembers["EmissiveColor"] = ResoniteMaterialComponentBuilder.CreateColorMember(
                    new ResoniteColor(1.0, 1.0, 1.0, 1.0));
            }
        }

        string materialContainerParentId = materialSlotParentId ?? materialSlotId;
        ResoniteLinkSceneBuilder.CreatedSlot createdMaterialSlot = await getOrCreateSharedChildSlotAsync(
            client,
            materialContainerParentId,
            materialSlotName,
            cancellationToken);
        materialContainerSlotId = createdMaterialSlot.SlotId;

        Component? existingMaterialComponent = await TryGetExistingMaterialComponentAsync(
            client,
            materialContainerSlotId,
            materialComponentType,
            cancellationToken);
        if (existingMaterialComponent is not null)
        {
            ReportProgress(
                $"[live] Material '{material.MaterialKey}' reusing existing component '{materialComponentType}'.");
            return new ResoniteLinkSceneBuilder.CreatedComponent(
                existingMaterialComponent.ID,
                materialComponentType);
        }

        if (albedoTextureUri is not null)
        {
            ResoniteLinkSceneBuilder.CreatedComponent albedoTexture = await CreateTextureComponentFromImportedUriAsync(
                client,
                materialContainerSlotId,
                albedoTextureUri,
                cancellationToken);
            materialMembers["AlbedoTexture"] = new Reference
            {
                TargetID = albedoTexture.ComponentId,
            };
        }

        if (normalTextureUri is not null)
        {
            ResoniteLinkSceneBuilder.CreatedComponent normalTexture = await CreateTextureComponentFromImportedUriAsync(
                client,
                materialContainerSlotId,
                normalTextureUri,
                cancellationToken);
            materialMembers["NormalMap"] = new Reference
            {
                TargetID = normalTexture.ComponentId,
            };
        }

        if (heightTextureUri is not null)
        {
            ResoniteLinkSceneBuilder.CreatedComponent heightTexture = await CreateTextureComponentFromImportedUriAsync(
                client,
                materialContainerSlotId,
                heightTextureUri,
                cancellationToken);
            materialMembers["HeightMap"] = new Reference
            {
                TargetID = heightTexture.ComponentId,
            };
        }

        if (metallicTextureUri is not null)
        {
            ResoniteLinkSceneBuilder.CreatedComponent metallicTexture = await CreateTextureComponentFromImportedUriAsync(
                client,
                materialContainerSlotId,
                metallicTextureUri,
                cancellationToken);
            Reference metallicReference = new()
            {
                TargetID = metallicTexture.ComponentId,
            };
            materialMembers["MetallicMap"] = metallicReference;
            materialMembers["OcclusionMap"] = new Reference
            {
                TargetID = metallicTexture.ComponentId,
            };
        }

        if (emissionTextureUri is not null)
        {
            ResoniteLinkSceneBuilder.CreatedComponent emissionTexture = await CreateTextureComponentFromImportedUriAsync(
                client,
                materialContainerSlotId,
                emissionTextureUri,
                cancellationToken);
            materialMembers["EmissiveMap"] = new Reference
            {
                TargetID = emissionTexture.ComponentId,
            };
        }

        ReportProgress(
            $"[live] Material '{material.MaterialKey}' creating component '{materialComponentType}' "
            + $"with {materialMembers.Count} members.");
        ResoniteLinkSceneBuilder.CreatedComponent materialComponent = await createComponentAsync(
            client,
            materialContainerSlotId,
            materialComponentType,
            materialMembers,
            cancellationToken);
        ReportProgress($"[live] Material '{material.MaterialKey}' ready.");
        return materialComponent;
    }

    private Task<ResoniteLinkSceneBuilder.CreatedComponent> CreateTextureComponentFromImportedUriAsync(
        IResoniteLinkClient client,
        string containerSlotId,
        Uri assetUri,
        CancellationToken cancellationToken)
    {
        return createComponentAsync(
            client,
            containerSlotId,
            "[FrooxEngine]FrooxEngine.StaticTexture2D",
            new Dictionary<string, Member>(StringComparer.Ordinal)
            {
                ["URL"] = new Field_Uri
                {
                    Value = assetUri,
                },
            },
            cancellationToken);
    }

    internal static TextureReferenceKey CreateTextureReferenceKey(string texturePath, ResoniteTextureSourceKind textureSourceKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(texturePath);
        return new TextureReferenceKey(textureSourceKind, texturePath);
    }

    private async Task<Component?> TryGetExistingMaterialComponentAsync(
        IResoniteLinkClient client,
        string slotId,
        string componentType,
        CancellationToken cancellationToken)
    {
        Slot? slot = await getSlotAsync(client, slotId, 1, cancellationToken);
        return slot?.Components?.FirstOrDefault(component =>
            string.Equals(component.ComponentType, componentType, StringComparison.Ordinal));
    }

    private void ReportProgress(string message)
    {
        progressReporter?.Invoke(message);
    }
}

internal sealed record CreatedMaterialAsset(
    string MaterialComponentId,
    string? MaterialPropertyBlockComponentId);
