using System.Collections.Concurrent;

using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Cli;

internal sealed class ResoniteMaterialAssetManager(
    string dataset,
    Func<string, string, CancellationToken, Task> ensureMaterialAssetSlotKnownAsync,
    Func<string, string, string, string, Func<Task<Uri>>, CancellationToken, Task<Uri>> ensureAssetComponentUrlKnownAsync,
    Func<string, string, string, IReadOnlyDictionary<string, Member>, CancellationToken, Task> ensureComponentKnownAsync)
{
    private const float DefaultNormalScale = 1.0f;
    private const float DefaultBundledHeightScale = 0.002f;
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> materialComponentTasks = new(StringComparer.Ordinal);

    public async Task<string> EnsureMaterialComponentAsync(
        IResoniteLinkClient client,
        ResoniteMaterialBinding material,
        IReadOnlyDictionary<string, string> preparedTexturePathsByKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(preparedTexturePathsByKey);

        string materialId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            dataset,
            "materialasset",
            material.MaterialKey);
        return await materialComponentTasks.GetOrAdd(
            material.MaterialKey,
            _ => new Lazy<Task<string>>(
                () => EnsureMaterialComponentCoreAsync(
                    client,
                    material,
                    preparedTexturePathsByKey,
                    materialId,
                    cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value.WaitAsync(cancellationToken);
    }

    private async Task<string> EnsureMaterialComponentCoreAsync(
        IResoniteLinkClient client,
        ResoniteMaterialBinding material,
        IReadOnlyDictionary<string, string> preparedTexturePathsByKey,
        string materialId,
        CancellationToken cancellationToken)
    {
        string materialSlotId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            dataset,
            "materialslot",
            material.MaterialKey);
        string textureComponentId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            dataset,
            "texture",
            material.MaterialKey);
        string emissionTextureComponentId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            dataset,
            "texture",
            $"{material.MaterialKey}-emission");
        string heightTextureComponentId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            dataset,
            "texture",
            $"{material.MaterialKey}-height");
        string metallicTextureComponentId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            dataset,
            "texture",
            $"{material.MaterialKey}-metallic");
        string normalTextureComponentId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            dataset,
            "texture",
            $"{material.MaterialKey}-normal");

        Dictionary<string, Member> materialMembers = ResoniteMaterialComponentBuilder.CreateMembers(material);
        string materialComponentType = ResoniteMaterialComponentBuilder.GetComponentType(material);

        await ensureMaterialAssetSlotKnownAsync(materialSlotId, material.MaterialKey, cancellationToken);

        if (material.TexturePath is not null
            && preparedTexturePathsByKey.TryGetValue(
                CreateTextureCacheKey(material.TexturePath, material.TextureSourceKind),
                out string? absoluteTexturePath))
        {
            await ensureAssetComponentUrlKnownAsync(
                materialSlotId,
                textureComponentId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                "URL",
                () => client.ImportTextureAsync(absoluteTexturePath, cancellationToken),
                cancellationToken);
            materialMembers["AlbedoTexture"] = new Reference
            {
                TargetID = textureComponentId,
            };
        }

        if (ResoniteMaterialComponentBuilder.TryGetBundledCompanionTextureSet(material, out BundledDefaultMaterialTextureSet? textureSet)
            && textureSet is not null)
        {
            if (textureSet.NormalPath is not null)
            {
                await ensureAssetComponentUrlKnownAsync(
                    materialSlotId,
                    normalTextureComponentId,
                    "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    "URL",
                    () => client.ImportTextureAsync(textureSet.NormalPath, cancellationToken),
                    cancellationToken);
                materialMembers["NormalMap"] = new Reference
                {
                    TargetID = normalTextureComponentId,
                };
                materialMembers["NormalScale"] = new Field_float
                {
                    Value = DefaultNormalScale,
                };
            }

            if (textureSet.HeightPath is not null
                && material.Projection == ResoniteMaterialProjection.Uv)
            {
                await ensureAssetComponentUrlKnownAsync(
                    materialSlotId,
                    heightTextureComponentId,
                    "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    "URL",
                    () => client.ImportTextureAsync(textureSet.HeightPath, cancellationToken),
                    cancellationToken);
                materialMembers["HeightMap"] = new Reference
                {
                    TargetID = heightTextureComponentId,
                };
                materialMembers["HeightScale"] = new Field_float
                {
                    Value = DefaultBundledHeightScale,
                };
            }

            if (textureSet.MetallicPath is not null)
            {
                await ensureAssetComponentUrlKnownAsync(
                    materialSlotId,
                    metallicTextureComponentId,
                    "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    "URL",
                    () => client.ImportTextureAsync(textureSet.MetallicPath, cancellationToken),
                    cancellationToken);
                Reference metallicReference = new()
                {
                    TargetID = metallicTextureComponentId,
                };
                materialMembers["MetallicMap"] = metallicReference;
                materialMembers["OcclusionMap"] = new Reference
                {
                    TargetID = metallicTextureComponentId,
                };
            }

            if (textureSet.EmissionPath is not null)
            {
                await ensureAssetComponentUrlKnownAsync(
                    materialSlotId,
                    emissionTextureComponentId,
                    "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    "URL",
                    () => client.ImportTextureAsync(textureSet.EmissionPath, cancellationToken),
                    cancellationToken);
                materialMembers["EmissiveMap"] = new Reference
                {
                    TargetID = emissionTextureComponentId,
                };
                materialMembers["EmissiveColor"] = ResoniteMaterialComponentBuilder.CreateColorMember(
                    new ResoniteColor(1.0, 1.0, 1.0, 1.0));
            }
        }

        await ensureComponentKnownAsync(
            materialSlotId,
            materialId,
            materialComponentType,
            materialMembers,
            cancellationToken);
        return materialId;
    }

    internal static string CreateTextureCacheKey(string? texturePath, ResoniteTextureSourceKind textureSourceKind)
    {
        return texturePath is null
            ? string.Empty
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{textureSourceKind}:{texturePath}");
    }
}
