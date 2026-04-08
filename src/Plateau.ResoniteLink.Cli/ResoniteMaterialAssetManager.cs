using System.Collections.Concurrent;

using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Cli;

internal sealed class ResoniteMaterialAssetManager(
    string dataset,
    Func<string, string, string, string, Func<Task<Uri>>, string, CancellationToken, Task<Uri>> ensureAssetComponentUrlKnownAsync,
    Func<string, string, string, IReadOnlyDictionary<string, Member>, CancellationToken, Task> ensureComponentKnownAsync)
{
    private const float DefaultNormalScale = 1.0f;
    private const float DefaultBundledHeightScale = 0.002f;
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> materialComponentTasks = new(StringComparer.Ordinal);

    public async Task<string> EnsureMaterialComponentAsync(
        IResoniteLinkClient client,
        ResoniteMaterialBinding material,
        IReadOnlyDictionary<string, (string AbsoluteTexturePath, string SourceFingerprint)> preparedTexturePathsByKey,
        string materialAssetSlotId,
        string materialInstanceKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(preparedTexturePathsByKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialAssetSlotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialInstanceKey);

        string materialId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            dataset,
            "materialasset",
            materialInstanceKey);
        return await materialComponentTasks.GetOrAdd(
            materialInstanceKey,
            _ => new Lazy<Task<string>>(
                () => EnsureMaterialComponentCoreAsync(
                    client,
                    material,
                    preparedTexturePathsByKey,
                    materialAssetSlotId,
                    materialInstanceKey,
                    materialId,
                    cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value.WaitAsync(cancellationToken);
    }

    private async Task<string> EnsureMaterialComponentCoreAsync(
        IResoniteLinkClient client,
        ResoniteMaterialBinding material,
        IReadOnlyDictionary<string, (string AbsoluteTexturePath, string SourceFingerprint)> preparedTexturePathsByKey,
        string materialAssetSlotId,
        string materialInstanceKey,
        string materialId,
        CancellationToken cancellationToken)
    {
        string textureComponentId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            dataset,
            "texture",
            materialInstanceKey);
        string emissionTextureComponentId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            dataset,
            "texture",
            $"{materialInstanceKey}-emission");
        string heightTextureComponentId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            dataset,
            "texture",
            $"{materialInstanceKey}-height");
        string metallicTextureComponentId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            dataset,
            "texture",
            $"{materialInstanceKey}-metallic");
        string normalTextureComponentId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(
            dataset,
            "texture",
            $"{materialInstanceKey}-normal");

        Dictionary<string, Member> materialMembers = ResoniteMaterialComponentBuilder.CreateMembers(material);
        string materialComponentType = ResoniteMaterialComponentBuilder.GetComponentType(material);

        if (material.TexturePath is not null
            && preparedTexturePathsByKey.TryGetValue(
                CreateTextureCacheKey(material.TexturePath, material.TextureSourceKind),
                out (string AbsoluteTexturePath, string SourceFingerprint) textureAsset))
        {
            await ensureAssetComponentUrlKnownAsync(
                materialAssetSlotId,
                textureComponentId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                "URL",
                () => client.ImportTextureAsync(textureAsset.AbsoluteTexturePath, cancellationToken),
                textureAsset.SourceFingerprint,
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
                    materialAssetSlotId,
                    normalTextureComponentId,
                    "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    "URL",
                    () => client.ImportTextureAsync(textureSet.NormalPath, cancellationToken),
                    textureSet.NormalPath,
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
                    materialAssetSlotId,
                    heightTextureComponentId,
                    "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    "URL",
                    () => client.ImportTextureAsync(textureSet.HeightPath, cancellationToken),
                    textureSet.HeightPath,
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
                    materialAssetSlotId,
                    metallicTextureComponentId,
                    "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    "URL",
                    () => client.ImportTextureAsync(textureSet.MetallicPath, cancellationToken),
                    textureSet.MetallicPath,
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
                    materialAssetSlotId,
                    emissionTextureComponentId,
                    "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    "URL",
                    () => client.ImportTextureAsync(textureSet.EmissionPath, cancellationToken),
                    textureSet.EmissionPath,
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
            materialAssetSlotId,
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
