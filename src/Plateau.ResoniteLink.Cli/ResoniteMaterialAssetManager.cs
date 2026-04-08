using System.Collections.Concurrent;
using System.Security.Cryptography;

using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Cli;

internal sealed class ResoniteMaterialAssetManager(
    string dataset,
    Func<string, string, string, string, Func<Task<Uri>>, string, CancellationToken, Task<Uri>> ensureAssetComponentUrlKnownAsync,
    Func<string, string, string, IReadOnlyDictionary<string, Member>, CancellationToken, Task> ensureComponentKnownAsync,
    Action<string>? progressReporter = null)
{
    private const float DefaultNormalScale = 1.0f;
    private const float DefaultBundledHeightScale = 0.002f;
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> materialComponentTasks = new(StringComparer.Ordinal);

    public async Task<string> EnsureMaterialComponentAsync(
        IResoniteLinkClient client,
        ResoniteMaterialBinding material,
        IReadOnlyDictionary<string, (ResoniteTextureImport TextureImport, string SourceFingerprint)> preparedTexturePathsByKey,
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
        ReportProgress($"[live] Material '{material.MaterialKey}' queued.");
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
        IReadOnlyDictionary<string, (ResoniteTextureImport TextureImport, string SourceFingerprint)> preparedTexturePathsByKey,
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
        ReportProgress(
            $"[live] Material '{material.MaterialKey}' resolving as '{materialComponentType}' "
            + $"(projection={material.Projection}, texture={material.TexturePath ?? "none"}).");

        if (material.TexturePath is not null
            && preparedTexturePathsByKey.TryGetValue(
                CreateTextureCacheKey(material.TexturePath, material.TextureSourceKind),
                out (ResoniteTextureImport TextureImport, string SourceFingerprint) textureAsset))
        {
            ReportProgress($"[live] Material '{material.MaterialKey}' importing albedo texture.");
            await ensureAssetComponentUrlKnownAsync(
                materialAssetSlotId,
                textureComponentId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                "URL",
                () => client.ImportTextureAsync(textureAsset.TextureImport, cancellationToken),
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
                string normalFingerprint = ComputeContentFingerprint(textureSet.NormalPath);
                ReportProgress(
                    $"[live] Material '{material.MaterialKey}' importing bundled normal map from '{textureSet.NormalPath}'.");
                await ensureAssetComponentUrlKnownAsync(
                    materialAssetSlotId,
                    normalTextureComponentId,
                    "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    "URL",
                    () => client.ImportTextureAsync(ResoniteTextureImportFactory.CreateFromFile(textureSet.NormalPath), cancellationToken),
                    normalFingerprint,
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
                string heightFingerprint = ComputeContentFingerprint(textureSet.HeightPath);
                ReportProgress(
                    $"[live] Material '{material.MaterialKey}' importing bundled height map from '{textureSet.HeightPath}'.");
                await ensureAssetComponentUrlKnownAsync(
                    materialAssetSlotId,
                    heightTextureComponentId,
                    "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    "URL",
                    () => client.ImportTextureAsync(ResoniteTextureImportFactory.CreateFromFile(textureSet.HeightPath), cancellationToken),
                    heightFingerprint,
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
                string metallicFingerprint = ComputeContentFingerprint(textureSet.MetallicPath);
                ReportProgress(
                    $"[live] Material '{material.MaterialKey}' importing bundled metallic map from '{textureSet.MetallicPath}'.");
                await ensureAssetComponentUrlKnownAsync(
                    materialAssetSlotId,
                    metallicTextureComponentId,
                    "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    "URL",
                    () => client.ImportTextureAsync(ResoniteTextureImportFactory.CreateFromFile(textureSet.MetallicPath), cancellationToken),
                    metallicFingerprint,
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
                string emissionFingerprint = ComputeContentFingerprint(textureSet.EmissionPath);
                ReportProgress(
                    $"[live] Material '{material.MaterialKey}' importing bundled emission map from '{textureSet.EmissionPath}'.");
                await ensureAssetComponentUrlKnownAsync(
                    materialAssetSlotId,
                    emissionTextureComponentId,
                    "[FrooxEngine]FrooxEngine.StaticTexture2D",
                    "URL",
                    () => client.ImportTextureAsync(ResoniteTextureImportFactory.CreateFromFile(textureSet.EmissionPath), cancellationToken),
                    emissionFingerprint,
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
            materialAssetSlotId,
            materialId,
            materialComponentType,
            materialMembers,
            cancellationToken);
        ReportProgress($"[live] Material '{material.MaterialKey}' ready.");
        return materialId;
    }

    internal static string CreateTextureCacheKey(string? texturePath, ResoniteTextureSourceKind textureSourceKind)
    {
        return texturePath is null
            ? string.Empty
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{textureSourceKind}:{texturePath}");
    }

    private static string ComputeContentFingerprint(string absolutePath)
    {
        using IncrementalHash incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using FileStream fileStream = new(
            absolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            useAsync: false);
        byte[] buffer = new byte[16 * 1024];
        int bytesRead;

        while ((bytesRead = fileStream.Read(buffer)) > 0)
        {
            incrementalHash.AppendData(buffer, 0, bytesRead);
        }

        return Convert.ToHexString(incrementalHash.GetHashAndReset());
    }

    private void ReportProgress(string message)
    {
        progressReporter?.Invoke(message);
    }
}
