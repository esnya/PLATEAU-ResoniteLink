using System.Collections.Concurrent;

using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Cli;

internal sealed class ResoniteMaterialAssetManager(
    Func<string, string, Func<CancellationToken, Task<Uri>>, CancellationToken, Task<ResoniteLinkSceneBuilder.CreatedComponent>> createSharedAssetComponentAsync,
    Func<string, string, Func<CancellationToken, Task<Uri>>, CancellationToken, Task<ResoniteLinkSceneBuilder.CreatedComponent>> createDedicatedAssetComponentAsync,
    Func<string, string, CancellationToken, Task<ResoniteLinkSceneBuilder.CreatedSlot>> getOrCreateSharedChildSlotAsync,
    Func<string, string, IReadOnlyDictionary<string, Member>, CancellationToken, Task<ResoniteLinkSceneBuilder.CreatedComponent>> createComponentAsync,
    Func<IResoniteLinkClient, ResoniteTextureImport, CancellationToken, Task<Uri>> importTextureAsync,
    Action<string>? progressReporter = null)
{
    private const float DefaultNormalScale = 1.0f;
    private const float DefaultBundledHeightScale = 0.002f;
    private readonly ConcurrentDictionary<(string ScopeSlotId, string MaterialSlotName), Lazy<Task<ResoniteLinkSceneBuilder.CreatedComponent>>> materialComponentTasks = [];

    public async Task<ResoniteLinkSceneBuilder.CreatedComponent> CreateMaterialComponentAsync(
        IResoniteLinkClient client,
        ResoniteMaterialBinding material,
        IReadOnlyDictionary<TextureReferenceKey, ResoniteTextureImport> preparedTexturePathsByKey,
        string materialSlotId,
        string? materialSlotParentId,
        string materialSlotName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(preparedTexturePathsByKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialSlotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialSlotName);
        ReportProgress($"[live] Material '{material.MaterialKey}' queued.");
        (string ScopeSlotId, string MaterialSlotName) materialTaskKey = (materialSlotId, materialSlotName);
        Lazy<Task<ResoniteLinkSceneBuilder.CreatedComponent>> materialTask = materialComponentTasks.GetOrAdd(
            materialTaskKey,
            _ => new Lazy<Task<ResoniteLinkSceneBuilder.CreatedComponent>>(
                () => CreateMaterialComponentCoreAsync(
                    client,
                    material,
                    preparedTexturePathsByKey,
                    materialSlotId,
                    materialSlotParentId,
                    materialSlotName,
                    CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication));
        Task<ResoniteLinkSceneBuilder.CreatedComponent> sharedTask = materialTask.Value;

        try
        {
            return await sharedTask.WaitAsync(cancellationToken);
        }
        catch
        {
            if (sharedTask.IsFaulted || sharedTask.IsCanceled)
            {
                materialComponentTasks.TryRemove(
                    new KeyValuePair<(string ScopeSlotId, string MaterialSlotName), Lazy<Task<ResoniteLinkSceneBuilder.CreatedComponent>>>(materialTaskKey, materialTask));
            }

            throw;
        }
    }

    private async Task<ResoniteLinkSceneBuilder.CreatedComponent> CreateMaterialComponentCoreAsync(
        IResoniteLinkClient client,
        ResoniteMaterialBinding material,
        IReadOnlyDictionary<TextureReferenceKey, ResoniteTextureImport> preparedTexturePathsByKey,
        string materialSlotId,
        string? materialSlotParentId,
        string materialSlotName,
        CancellationToken cancellationToken)
    {
        string materialContainerSlotId = materialSlotId;
        if (materialSlotParentId is not null)
        {
            ResoniteLinkSceneBuilder.CreatedSlot createdMaterialSlot = await getOrCreateSharedChildSlotAsync(
                materialSlotParentId,
                materialSlotName,
                cancellationToken);
            materialContainerSlotId = createdMaterialSlot.SlotId;
        }

        Func<string, string, Func<CancellationToken, Task<Uri>>, CancellationToken, Task<ResoniteLinkSceneBuilder.CreatedComponent>> createAssetComponentAsync =
            materialSlotParentId is null
                ? createDedicatedAssetComponentAsync
                : createSharedAssetComponentAsync;
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

        if (material.TexturePath is not null
            && preparedTexturePathsByKey.TryGetValue(
                CreateTextureReferenceKey(material.TexturePath, material.TextureSourceKind),
                out ResoniteTextureImport? textureAsset))
        {
            ReportProgress($"[live] Material '{material.MaterialKey}' importing albedo texture.");
            ResoniteLinkSceneBuilder.CreatedComponent albedoTexture = await createTextureComponentAsync(
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ct => importTextureAsync(client, textureAsset, ct),
                cancellationToken);
            materialMembers["AlbedoTexture"] = new Reference
            {
                TargetID = albedoTexture.ComponentId,
            };
        }

        if (ResoniteMaterialComponentBuilder.TryGetBundledCompanionTextureSet(material, out BundledDefaultMaterialTextureSet? textureSet)
            && textureSet is not null)
        {
            if (textureSet.NormalPath is not null)
            {
                ReportProgress(
                    $"[live] Material '{material.MaterialKey}' importing bundled normal map from '{textureSet.NormalPath}'.");
                ResoniteLinkSceneBuilder.CreatedComponent normalTexture = await createTextureComponentAsync(
                    "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    ct => importTextureAsync(
                        client,
                        ResoniteTextureImportFactory.CreateFromFile(textureSet.NormalPath),
                        ct),
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

            if (textureSet.HeightPath is not null
                && material.Projection == ResoniteMaterialProjection.Uv)
            {
                ReportProgress(
                    $"[live] Material '{material.MaterialKey}' importing bundled height map from '{textureSet.HeightPath}'.");
                ResoniteLinkSceneBuilder.CreatedComponent heightTexture = await createTextureComponentAsync(
                    "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    ct => importTextureAsync(
                        client,
                        ResoniteTextureImportFactory.CreateFromFile(textureSet.HeightPath),
                        ct),
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

            if (textureSet.MetallicPath is not null)
            {
                ReportProgress(
                    $"[live] Material '{material.MaterialKey}' importing bundled metallic map from '{textureSet.MetallicPath}'.");
                ResoniteLinkSceneBuilder.CreatedComponent metallicTexture = await createTextureComponentAsync(
                    "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    ct => importTextureAsync(
                        client,
                        ResoniteTextureImportFactory.CreateFromFile(textureSet.MetallicPath),
                        ct),
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

            if (textureSet.EmissionPath is not null)
            {
                ReportProgress(
                    $"[live] Material '{material.MaterialKey}' importing bundled emission map from '{textureSet.EmissionPath}'.");
                ResoniteLinkSceneBuilder.CreatedComponent emissionTexture = await createTextureComponentAsync(
                    "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    ct => importTextureAsync(
                        client,
                        ResoniteTextureImportFactory.CreateFromFile(textureSet.EmissionPath),
                        ct),
                    cancellationToken);
                materialMembers["EmissiveMap"] = new Reference
                {
                    TargetID = emissionTexture.ComponentId,
                };
                materialMembers["EmissiveColor"] = ResoniteMaterialComponentBuilder.CreateColorMember(
                    new ResoniteColor(1.0, 1.0, 1.0, 1.0));
            }
        }

        ReportProgress(
            $"[live] Material '{material.MaterialKey}' creating component '{materialComponentType}' "
            + $"with {materialMembers.Count} members.");
        ResoniteLinkSceneBuilder.CreatedComponent materialComponent = await createComponentAsync(
            materialContainerSlotId,
            materialComponentType,
            materialMembers,
            cancellationToken);
        ReportProgress($"[live] Material '{material.MaterialKey}' ready.");
        return materialComponent;
    }

    internal static TextureReferenceKey CreateTextureReferenceKey(string texturePath, ResoniteTextureSourceKind textureSourceKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(texturePath);
        return new TextureReferenceKey(textureSourceKind, texturePath);
    }

    private void ReportProgress(string message)
    {
        progressReporter?.Invoke(message);
    }
}
