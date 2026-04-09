using System.Collections.Concurrent;

using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Cli;

internal sealed class ResoniteMaterialAssetManager(
    Func<string, string, string, string, Func<Task<Uri>>, CancellationToken, Task<Uri>> ensureStaticAssetComponentUrlKnownAsync,
    Func<string, string, string, string, Func<Task<Uri>>, CancellationToken, Task<Uri>> upsertDedicatedAssetComponentUrlAsync,
    Func<string, string, string, CancellationToken, Task> ensureAssetSlotKnownAsync,
    Func<string, string, string, IReadOnlyDictionary<string, Member>, CancellationToken, Task> ensureComponentKnownAsync,
    Action<string>? progressReporter = null)
{
    private const float DefaultNormalScale = 1.0f;
    private const float DefaultBundledHeightScale = 0.002f;
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> materialComponentTasks = new(StringComparer.Ordinal);

    public async Task<string> EnsureMaterialComponentAsync(
        IResoniteLinkClient client,
        ResoniteMaterialBinding material,
        IReadOnlyDictionary<string, ResoniteTextureImport> preparedTexturePathsByKey,
        string materialSlotId,
        string? materialSlotParentId,
        string materialSlotName,
        string materialInstanceKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(preparedTexturePathsByKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialSlotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialSlotName);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialInstanceKey);

        string materialId = CreateAssetComponentId(materialSlotId, materialInstanceKey, "material");
        ReportProgress($"[live] Material '{material.MaterialKey}' queued.");
        return await materialComponentTasks.GetOrAdd(
            materialInstanceKey,
            _ => new Lazy<Task<string>>(
                () => EnsureMaterialComponentCoreAsync(
                    client,
                    material,
                    preparedTexturePathsByKey,
                    materialSlotId,
                    materialSlotParentId,
                    materialSlotName,
                    materialInstanceKey,
                    materialId,
                    cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value.WaitAsync(cancellationToken);
    }

    private async Task<string> EnsureMaterialComponentCoreAsync(
        IResoniteLinkClient client,
        ResoniteMaterialBinding material,
        IReadOnlyDictionary<string, ResoniteTextureImport> preparedTexturePathsByKey,
        string materialSlotId,
        string? materialSlotParentId,
        string materialSlotName,
        string materialInstanceKey,
        string materialId,
        CancellationToken cancellationToken)
    {
        if (materialSlotParentId is not null)
        {
            await ensureAssetSlotKnownAsync(materialSlotId, materialSlotParentId, materialSlotName, cancellationToken);
        }

        string textureComponentId = CreateAssetComponentId(materialSlotId, materialInstanceKey, "albedo");
        string emissionTextureComponentId = CreateAssetComponentId(materialSlotId, materialInstanceKey, "emission");
        string heightTextureComponentId = CreateAssetComponentId(materialSlotId, materialInstanceKey, "height");
        string metallicTextureComponentId = CreateAssetComponentId(materialSlotId, materialInstanceKey, "metallic");
        string normalTextureComponentId = CreateAssetComponentId(materialSlotId, materialInstanceKey, "normal");
        Func<string, string, string, string, Func<Task<Uri>>, CancellationToken, Task<Uri>> ensureAssetComponentUrlAsync =
            materialSlotParentId is null
                ? upsertDedicatedAssetComponentUrlAsync
                : ensureStaticAssetComponentUrlKnownAsync;

        Dictionary<string, Member> materialMembers = ResoniteMaterialComponentBuilder.CreateMembers(material);
        string materialComponentType = ResoniteMaterialComponentBuilder.GetComponentType(material);
        ReportProgress(
            $"[live] Material '{material.MaterialKey}' resolving as '{materialComponentType}' "
            + $"(projection={material.Projection}, texture={material.TexturePath ?? "none"}).");

        if (material.TexturePath is not null
            && preparedTexturePathsByKey.TryGetValue(
                CreateTextureCacheKey(material.TexturePath, material.TextureSourceKind),
                out ResoniteTextureImport? textureAsset))
        {
            ReportProgress($"[live] Material '{material.MaterialKey}' importing albedo texture.");
            await ensureAssetComponentUrlAsync(
                materialSlotId,
                textureComponentId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                "URL",
                () => client.ImportTextureAsync(textureAsset, cancellationToken),
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
                ReportProgress(
                    $"[live] Material '{material.MaterialKey}' importing bundled normal map from '{textureSet.NormalPath}'.");
                await ensureAssetComponentUrlAsync(
                    materialSlotId,
                    normalTextureComponentId,
                    "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    "URL",
                    () => client.ImportTextureAsync(
                        ResoniteTextureImportFactory.CreateFromFile(textureSet.NormalPath),
                        cancellationToken),
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
                ReportProgress(
                    $"[live] Material '{material.MaterialKey}' importing bundled height map from '{textureSet.HeightPath}'.");
                await ensureAssetComponentUrlAsync(
                    materialSlotId,
                    heightTextureComponentId,
                    "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    "URL",
                    () => client.ImportTextureAsync(
                        ResoniteTextureImportFactory.CreateFromFile(textureSet.HeightPath),
                        cancellationToken),
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
                ReportProgress(
                    $"[live] Material '{material.MaterialKey}' importing bundled metallic map from '{textureSet.MetallicPath}'.");
                await ensureAssetComponentUrlAsync(
                    materialSlotId,
                    metallicTextureComponentId,
                    "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    "URL",
                    () => client.ImportTextureAsync(
                        ResoniteTextureImportFactory.CreateFromFile(textureSet.MetallicPath),
                        cancellationToken),
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
                ReportProgress(
                    $"[live] Material '{material.MaterialKey}' importing bundled emission map from '{textureSet.EmissionPath}'.");
                await ensureAssetComponentUrlAsync(
                    materialSlotId,
                    emissionTextureComponentId,
                    "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    "URL",
                    () => client.ImportTextureAsync(
                        ResoniteTextureImportFactory.CreateFromFile(textureSet.EmissionPath),
                        cancellationToken),
                    cancellationToken);
                materialMembers["EmissiveMap"] = new Reference
                {
                    TargetID = emissionTextureComponentId,
                };
                materialMembers["EmissiveColor"] = ResoniteMaterialComponentBuilder.CreateColorMember(
                    new ResoniteColor(1.0, 1.0, 1.0, 1.0));
            }
        }

        ReportProgress(
            $"[live] Material '{material.MaterialKey}' ensuring component '{materialComponentType}' "
            + $"with {materialMembers.Count} members.");
        await ensureComponentKnownAsync(
            materialSlotId,
            materialId,
            materialComponentType,
            materialMembers,
            cancellationToken);
        ReportProgress($"[live] Material '{material.MaterialKey}' ready.");
        return materialId;
    }

    private static string CreateAssetComponentId(string assetSlotId, string materialInstanceKey, string role)
    {
        return $"{assetSlotId}_{materialInstanceKey}_{role}";
    }

    internal static string CreateTextureCacheKey(string? texturePath, ResoniteTextureSourceKind textureSourceKind)
    {
        return texturePath is null
            ? string.Empty
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{textureSourceKind}:{texturePath}");
    }

    private void ReportProgress(string message)
    {
        progressReporter?.Invoke(message);
    }
}
