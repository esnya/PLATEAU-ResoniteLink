using System.Globalization;
using System.Security.Cryptography;
using System.Text;

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
    private readonly AsyncCompletedResultCache<(string ScopeSlotId, string PropertyBlockSlotName), ResoniteLinkSceneBuilder.CreatedComponent> propertyBlockComponentCache = new();

    public async Task<CreatedMaterialAsset> CreateMaterialComponentAsync(
        IResoniteLinkClient client,
        ResoniteMaterialBinding material,
        IReadOnlyDictionary<TextureReferenceKey, ResoniteTextureImport> preparedTexturePathsByKey,
        string materialSlotId,
        string? materialSlotParentId,
        string materialSlotName,
        string propertyBlockScopeSlotId,
        bool useSharedAlbedoOnlyPropertyBlock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(preparedTexturePathsByKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialSlotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialSlotName);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyBlockScopeSlotId);
        ReportProgress($"[live] Material '{material.MaterialKey}' queued.");

        ResoniteLinkSceneBuilder.CreatedComponent materialComponent = await GetOrCreateMaterialComponentAsync(
            client,
            material,
            preparedTexturePathsByKey,
            materialSlotId,
            materialSlotParentId,
            materialSlotName,
            suppressAlbedoTexture: useSharedAlbedoOnlyPropertyBlock,
            cancellationToken: cancellationToken);
        if (!useSharedAlbedoOnlyPropertyBlock)
        {
            return new CreatedMaterialAsset(materialComponent.ComponentId, null);
        }

        if (!preparedTexturePathsByKey.TryGetValue(
            CreateTextureReferenceKey(material.TexturePath!, material.TextureSourceKind),
            out ResoniteTextureImport? textureAsset))
        {
            throw new InvalidOperationException(
                $"Prepared albedo texture was missing for shared albedo-only material '{material.MaterialKey}'.");
        }

        TextureReferenceKey textureReferenceKey = CreateTextureReferenceKey(material.TexturePath!, material.TextureSourceKind);
        ResoniteLinkSceneBuilder.CreatedComponent propertyBlock = await GetOrCreateMainTexturePropertyBlockAsync(
            client,
            propertyBlockScopeSlotId,
            CreateMainTexturePropertyBlockSlotName(textureReferenceKey, textureAsset),
            textureAsset,
            cancellationToken);
        return new CreatedMaterialAsset(materialComponent.ComponentId, propertyBlock.ComponentId);
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
        string materialContainerParentId = materialSlotParentId ?? materialSlotId;

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

        Slot? existingMaterialSlot = await TryGetExistingChildSlotAsync(
            client,
            materialContainerParentId,
            materialSlotName,
            cancellationToken);
        if (existingMaterialSlot is not null)
        {
            materialContainerSlotId = existingMaterialSlot.ID;
            Component? existingMaterialComponent = await TryGetExistingComponentAsync(
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
        }

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
                    ResoniteTextureImportFactory.CreateFromFile(textureSet.NormalPath),
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
                    ResoniteTextureImportFactory.CreateFromFile(textureSet.HeightPath),
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
                    ResoniteTextureImportFactory.CreateFromFile(textureSet.MetallicPath),
                    cancellationToken);
            }

            if (textureSet.EmissionPath is not null)
            {
                ReportProgress(
                    $"[live] Material '{material.MaterialKey}' importing bundled emission map from '{textureSet.EmissionPath}'.");
                emissionTextureUri = await importTextureAsync(
                    client,
                    ResoniteTextureImportFactory.CreateFromFile(textureSet.EmissionPath),
                    cancellationToken);
                materialMembers["EmissiveColor"] = ResoniteMaterialComponentBuilder.CreateColorMember(
                    new ResoniteColor(1.0, 1.0, 1.0, 1.0));
            }
        }

        if (existingMaterialSlot is null)
        {
            ResoniteLinkSceneBuilder.CreatedSlot createdMaterialSlot = await getOrCreateSharedChildSlotAsync(
                client,
                materialContainerParentId,
                materialSlotName,
                cancellationToken);
            materialContainerSlotId = createdMaterialSlot.SlotId;
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

    private async Task<ResoniteLinkSceneBuilder.CreatedComponent> GetOrCreateMainTexturePropertyBlockAsync(
        IResoniteLinkClient client,
        string propertyBlockScopeSlotId,
        string propertyBlockSlotName,
        ResoniteTextureImport textureAsset,
        CancellationToken cancellationToken)
    {
        return await propertyBlockComponentCache.GetOrCreateAsync(
            (propertyBlockScopeSlotId, propertyBlockSlotName),
            ct => CreateMainTexturePropertyBlockCoreAsync(
                client,
                propertyBlockScopeSlotId,
                propertyBlockSlotName,
                textureAsset,
                ct),
            cancellationToken);
    }

    private async Task<ResoniteLinkSceneBuilder.CreatedComponent> CreateMainTexturePropertyBlockCoreAsync(
        IResoniteLinkClient client,
        string propertyBlockScopeSlotId,
        string propertyBlockSlotName,
        ResoniteTextureImport textureAsset,
        CancellationToken cancellationToken)
    {
        ResoniteLinkSceneBuilder.CreatedSlot propertyBlockSlot = await getOrCreateSharedChildSlotAsync(
            client,
            propertyBlockScopeSlotId,
            propertyBlockSlotName,
            cancellationToken);

        Component? existingPropertyBlock = await TryGetExistingComponentAsync(
            client,
            propertyBlockSlot.SlotId,
            "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock",
            cancellationToken);
        if (existingPropertyBlock is not null)
        {
            return new ResoniteLinkSceneBuilder.CreatedComponent(
                existingPropertyBlock.ID,
                existingPropertyBlock.ComponentType);
        }

        Component? existingTextureComponent = await TryGetExistingComponentAsync(
            client,
            propertyBlockSlot.SlotId,
            "[FrooxEngine]FrooxEngine.StaticTexture2D",
            cancellationToken);
        ResoniteLinkSceneBuilder.CreatedComponent textureComponent = existingTextureComponent is not null
            ? new ResoniteLinkSceneBuilder.CreatedComponent(
                existingTextureComponent.ID,
                existingTextureComponent.ComponentType)
            : await createDedicatedAssetComponentAsync(
                client,
                propertyBlockSlot.SlotId,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                ct => importTextureAsync(client, textureAsset, ct),
                cancellationToken);

        return await createComponentAsync(
            client,
            propertyBlockSlot.SlotId,
            "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock",
            new Dictionary<string, Member>(StringComparer.Ordinal)
            {
                ["Texture"] = new Reference
                {
                    TargetID = textureComponent.ComponentId,
                },
            },
            cancellationToken);
    }

    private static string CreateMainTexturePropertyBlockSlotName(
        TextureReferenceKey textureReferenceKey,
        ResoniteTextureImport textureImport)
    {
        byte[] digest = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                $"{textureReferenceKey.SourceKind}:{textureReferenceKey.TexturePath}|{CreateTextureImportFingerprint(textureImport)}"));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"main-texture-property-block_{Convert.ToHexString(digest.AsSpan(0, 8)).ToLowerInvariant()}");
    }

    private static string CreateTextureImportFingerprint(ResoniteTextureImport textureImport)
    {
        return textureImport switch
        {
            ResoniteFileTextureImport fileImport => CreateFileTextureFingerprint(fileImport.AbsolutePath),
            ResoniteRawTextureImport rawImport => string.Create(
                CultureInfo.InvariantCulture,
                $"raw:{rawImport.Identity ?? "none"}:{rawImport.ColorProfile}:{rawImport.Width}:{rawImport.Height}:{rawImport.RawRgba32Bytes.Length}"),
            ResoniteRawHdrTextureImport rawHdrImport => string.Create(
                CultureInfo.InvariantCulture,
                $"raw-hdr:{rawHdrImport.Width}:{rawHdrImport.Height}:{rawHdrImport.RawRgbaFloatBytes.Length}"),
            _ => textureImport.GetType().FullName ?? textureImport.GetType().Name,
        };
    }

    private static string CreateFileTextureFingerprint(string absolutePath)
    {
        string normalizedPath = Path.GetFullPath(absolutePath);
        try
        {
            FileInfo fileInfo = new(normalizedPath);
            return fileInfo.Exists
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"{normalizedPath}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}")
                : normalizedPath;
        }
        catch (IOException)
        {
            return normalizedPath;
        }
        catch (UnauthorizedAccessException)
        {
            return normalizedPath;
        }
        catch (NotSupportedException)
        {
            return normalizedPath;
        }
        catch (System.Security.SecurityException)
        {
            return normalizedPath;
        }
    }

    private async Task<Component?> TryGetExistingComponentAsync(
        IResoniteLinkClient client,
        string slotId,
        string componentType,
        CancellationToken cancellationToken)
    {
        Slot? slot = await getSlotAsync(client, slotId, 1, cancellationToken);
        return slot?.Components?.FirstOrDefault(component =>
            string.Equals(component.ComponentType, componentType, StringComparison.Ordinal));
    }

    private async Task<Slot?> TryGetExistingChildSlotAsync(
        IResoniteLinkClient client,
        string parentSlotId,
        string childSlotName,
        CancellationToken cancellationToken)
    {
        Slot? parentSlot = await getSlotAsync(client, parentSlotId, 1, cancellationToken);
        return parentSlot?.Children?.FirstOrDefault(slot =>
            string.Equals(slot.Name?.Value, childSlotName, StringComparison.Ordinal));
    }

    private void ReportProgress(string message)
    {
        progressReporter?.Invoke(message);
    }
}

internal sealed record CreatedMaterialAsset(
    string MaterialComponentId,
    string? MaterialPropertyBlockComponentId);
