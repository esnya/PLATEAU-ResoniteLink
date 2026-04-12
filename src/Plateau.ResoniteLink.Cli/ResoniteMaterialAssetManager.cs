using System.Diagnostics;

using Plateau.ResoniteLink.Application.Logging;
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
        Stopwatch totalStopwatch = Stopwatch.StartNew();
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

        Stopwatch lookupStopwatch = Stopwatch.StartNew();
        ResoniteLinkSceneBuilder.CreatedComponent? reusedSharedMaterial = await TryReuseExistingSharedMaterialComponentAsync(
            client,
            materialSlotParentId,
            materialSlotName,
            materialComponentType,
            cancellationToken);
        lookupStopwatch.Stop();
        if (reusedSharedMaterial is not null)
        {
            totalStopwatch.Stop();
            ReportProgress(
                PlateauLog.Debug(
                    "live",
                    $"Material '{material.MaterialKey}' phase timings: "
                    + $"lookup_s={lookupStopwatch.Elapsed.TotalSeconds:F3} "
                    + $"texture_imports_s=0.000 component_create_s=0.000 total_s={totalStopwatch.Elapsed.TotalSeconds:F3} "
                    + "reused_existing=true."));
            return reusedSharedMaterial.Value;
        }

        Stopwatch textureImportStopwatch = Stopwatch.StartNew();
        Task<Uri?> albedoTextureTask = Task.FromResult<Uri?>(null);
        Task<Uri?> normalTextureTask = Task.FromResult<Uri?>(null);
        Task<Uri?> heightTextureTask = Task.FromResult<Uri?>(null);
        Task<Uri?> metallicTextureTask = Task.FromResult<Uri?>(null);
        Task<Uri?> emissionTextureTask = Task.FromResult<Uri?>(null);

        if (!suppressAlbedoTexture
            && material.TexturePath is not null
            && TryResolveAlbedoTextureImport(
                material,
                preparedTexturePathsByKey,
                out ResoniteTextureImport? textureAsset))
        {
            ReportProgress($"[live] Material '{material.MaterialKey}' importing albedo texture.");
            albedoTextureTask = ImportOptionalTextureAsync(client, textureAsset, cancellationToken);
        }

        if (ResoniteMaterialComponentBuilder.TryGetBundledCompanionTextureSet(material, out BundledDefaultMaterialTextureSet? textureSet)
            && textureSet is not null)
        {
            if (textureSet.NormalPath is not null)
            {
                ReportProgress(
                    $"[live] Material '{material.MaterialKey}' importing bundled normal map from '{textureSet.NormalPath}'.");
                normalTextureTask = ImportOptionalTextureAsync(
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
                heightTextureTask = ImportOptionalTextureAsync(
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
                metallicTextureTask = ImportOptionalTextureAsync(
                    client,
                    ResoniteTextureImportFactory.CreateFromFile(textureSet.MetallicPath),
                    cancellationToken);
            }

            if (textureSet.EmissionPath is not null)
            {
                ReportProgress(
                    $"[live] Material '{material.MaterialKey}' importing bundled emission map from '{textureSet.EmissionPath}'.");
                emissionTextureTask = ImportOptionalTextureAsync(
                    client,
                    ResoniteTextureImportFactory.CreateFromFile(textureSet.EmissionPath),
                    cancellationToken);
                materialMembers["EmissiveColor"] = ResoniteMaterialComponentBuilder.CreateColorMember(
                    new ResoniteColor(1.0, 1.0, 1.0, 1.0));
            }
        }
        await Task.WhenAll(
            albedoTextureTask,
            normalTextureTask,
            heightTextureTask,
            metallicTextureTask,
            emissionTextureTask);
        Uri? albedoTextureUri = await albedoTextureTask;
        Uri? normalTextureUri = await normalTextureTask;
        Uri? heightTextureUri = await heightTextureTask;
        Uri? metallicTextureUri = await metallicTextureTask;
        Uri? emissionTextureUri = await emissionTextureTask;
        textureImportStopwatch.Stop();

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

        Stopwatch componentCreateStopwatch = Stopwatch.StartNew();
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
        componentCreateStopwatch.Stop();
        totalStopwatch.Stop();
        ReportProgress(
            PlateauLog.Debug(
                "live",
                $"Material '{material.MaterialKey}' phase timings: "
                + $"lookup_s={lookupStopwatch.Elapsed.TotalSeconds:F3} "
                + $"texture_imports_s={textureImportStopwatch.Elapsed.TotalSeconds:F3} "
                + $"component_create_s={componentCreateStopwatch.Elapsed.TotalSeconds:F3} "
                + $"total_s={totalStopwatch.Elapsed.TotalSeconds:F3} reused_existing=false."));
        ReportProgress($"[live] Material '{material.MaterialKey}' ready.");
        return materialComponent;
    }

    private async Task<ResoniteLinkSceneBuilder.CreatedComponent?> TryReuseExistingSharedMaterialComponentAsync(
        IResoniteLinkClient client,
        string? materialSlotParentId,
        string materialSlotName,
        string materialComponentType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(materialSlotParentId))
        {
            return null;
        }

        Slot? parentSlot = await getSlotAsync(client, materialSlotParentId, 1, cancellationToken);
        if (parentSlot?.Children is null)
        {
            return null;
        }

        Slot[] matchingSlots = parentSlot.Children
            .Where(child => string.Equals(child.Name?.Value, materialSlotName, StringComparison.Ordinal))
            .ToArray();
        if (matchingSlots.Length == 0)
        {
            return null;
        }

        if (matchingSlots.Length > 1)
        {
            throw new InvalidOperationException(
                $"Parent slot '{materialSlotParentId}' contains multiple child slots named '{materialSlotName}'.");
        }

        Component? existingMaterialComponent = matchingSlots[0].Components?.FirstOrDefault(component =>
            string.Equals(component.ComponentType, materialComponentType, StringComparison.Ordinal));
        if (existingMaterialComponent is null)
        {
            return null;
        }

        ReportProgress(
            $"[live] Material slot '{materialSlotName}' reusing existing component '{materialComponentType}' without texture import.");
        return new ResoniteLinkSceneBuilder.CreatedComponent(
            existingMaterialComponent.ID,
            materialComponentType);
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

    private Task<Uri?> ImportOptionalTextureAsync(
        IResoniteLinkClient client,
        ResoniteTextureImport textureImport,
        CancellationToken cancellationToken)
    {
        return ImportOptionalTextureCoreAsync(client, textureImport, cancellationToken);
    }

    private async Task<Uri?> ImportOptionalTextureCoreAsync(
        IResoniteLinkClient client,
        ResoniteTextureImport textureImport,
        CancellationToken cancellationToken)
    {
        return await importTextureAsync(client, textureImport, cancellationToken);
    }

    internal static TextureReferenceKey CreateTextureReferenceKey(string texturePath, ResoniteTextureSourceKind textureSourceKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(texturePath);
        return new TextureReferenceKey(textureSourceKind, texturePath);
    }

    private static bool TryResolveAlbedoTextureImport(
        ResoniteMaterialBinding material,
        IReadOnlyDictionary<TextureReferenceKey, ResoniteTextureImport> preparedTexturePathsByKey,
        out ResoniteTextureImport textureImport)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(preparedTexturePathsByKey);

        if (material.TexturePath is not null
            && preparedTexturePathsByKey.TryGetValue(
                CreateTextureReferenceKey(material.TexturePath, material.TextureSourceKind),
                out ResoniteTextureImport? preparedTextureImport))
        {
            textureImport = preparedTextureImport;
            return true;
        }

        if (material.TextureSourceKind == ResoniteTextureSourceKind.Bundled
            && material.TexturePath is not null)
        {
            textureImport = ResoniteTextureImportFactory.CreateFromFile(
                BundledDefaultMaterialAssetStore.GetAbsolutePath(material.TexturePath));
            return true;
        }

        textureImport = null!;
        return false;
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
