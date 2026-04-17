using System.Diagnostics;

using Plateau.ResoniteLink.Application.Logging;
using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Targets.Resonite;

internal sealed class ResoniteMaterialAssetManager(
    Func<IResoniteLinkClient, string, string, Func<CancellationToken, Task<Uri>>, CancellationToken, Task<CreatedComponent>> createSharedAssetComponentAsync,
    Func<IResoniteLinkClient, string, string, Func<CancellationToken, Task<Uri>>, CancellationToken, Task<CreatedComponent>> createDedicatedAssetComponentAsync,
    Func<IResoniteLinkClient, string, string, CancellationToken, Task<CreatedSlot>> getOrCreateSharedChildSlotAsync,
    Func<IResoniteLinkClient, string, string, IReadOnlyDictionary<string, Member>, CancellationToken, Task<CreatedComponent>> createComponentAsync,
    Func<IResoniteLinkClient, ResoniteTextureImport, CancellationToken, Task<Uri>> importTextureAsync,
    Action<string>? progressReporter = null)
{
    private const float DefaultNormalScale = 1.0f;
    private const float DefaultBundledHeightScale = 0.002f;
    private readonly AsyncCompletedResultCache<(string ScopeSlotId, string MaterialSlotName, string MaterialComponentType), CreatedComponent> materialComponentCache = new();

    public async Task<CreatedMaterialAsset> CreateMaterialComponentAsync(
        IResoniteLinkClient client,
        ResoniteMaterialBinding material,
        IReadOnlyDictionary<string, ResoniteTextureImport> preparedTextureImportsByIdentity,
        IReadOnlyDictionary<TerrainTextureOverlay, ResoniteTextureImport> preparedTerrainTextureImportsByOverlay,
        string materialSlotId,
        string? materialSlotParentId,
        string materialSlotName,
        string rendererSlotId,
        string textureOverrideAssetSlotId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(preparedTextureImportsByIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialSlotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialSlotName);
        ArgumentException.ThrowIfNullOrWhiteSpace(rendererSlotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(textureOverrideAssetSlotId);
        ReportProgress($"[live] Material '{material.MaterialKey}' queued.");
        string materialComponentType = ResoniteMaterialComponentBuilder.GetComponentType(material);

        CreatedComponent materialComponent = await GetOrCreateMaterialComponentAsync(
            client,
            material,
            preparedTextureImportsByIdentity,
            preparedTerrainTextureImportsByOverlay,
            materialSlotId,
            materialSlotParentId,
            materialSlotName,
            materialComponentType,
            cancellationToken: cancellationToken);
        return new CreatedMaterialAsset(materialComponent.ComponentId, null);
    }

    private async Task<CreatedComponent> GetOrCreateMaterialComponentAsync(
        IResoniteLinkClient client,
        ResoniteMaterialBinding material,
        IReadOnlyDictionary<string, ResoniteTextureImport> preparedTextureImportsByIdentity,
        IReadOnlyDictionary<TerrainTextureOverlay, ResoniteTextureImport> preparedTerrainTextureImportsByOverlay,
        string materialSlotId,
        string? materialSlotParentId,
        string materialSlotName,
        string materialComponentType,
        bool suppressAlbedoTexture = false,
        CancellationToken cancellationToken = default)
    {
        (string ScopeSlotId, string MaterialSlotName, string MaterialComponentType) materialTaskKey = (
            materialSlotId,
            materialSlotName,
            materialComponentType);
        return await materialComponentCache.GetOrCreateAsync(
            materialTaskKey,
            ct => CreateMaterialComponentCoreAsync(
                client,
                material,
                preparedTextureImportsByIdentity,
                preparedTerrainTextureImportsByOverlay,
                materialSlotId,
                materialSlotParentId,
                materialSlotName,
                materialComponentType,
                suppressAlbedoTexture,
                ct),
            cancellationToken);
    }

    private async Task<CreatedComponent> CreateMaterialComponentCoreAsync(
        IResoniteLinkClient client,
        ResoniteMaterialBinding material,
        IReadOnlyDictionary<string, ResoniteTextureImport> preparedTextureImportsByIdentity,
        IReadOnlyDictionary<TerrainTextureOverlay, ResoniteTextureImport> preparedTerrainTextureImportsByOverlay,
        string materialSlotId,
        string? materialSlotParentId,
        string materialSlotName,
        string materialComponentType,
        bool suppressAlbedoTexture,
        CancellationToken cancellationToken)
    {
        Stopwatch totalStopwatch = Stopwatch.StartNew();
        string materialContainerSlotId = materialSlotId;

        Func<string, string, Func<CancellationToken, Task<Uri>>, CancellationToken, Task<CreatedComponent>> createAssetComponentAsync =
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
        Func<string, Func<CancellationToken, Task<Uri>>, CancellationToken, Task<CreatedComponent>> createTextureComponentAsync =
            (componentType, importAssetAsync, ct) => createAssetComponentAsync(
                materialContainerSlotId,
                componentType,
                importAssetAsync,
                ct);

        Dictionary<string, Member> materialMembers = ResoniteMaterialComponentBuilder.CreateMembers(material);
        ReportProgress(
            $"[live] Material '{material.MaterialKey}' resolving as '{materialComponentType}' "
            + $"(projection={material.Projection}, texture={(material.TerrainOverlay is not null ? "projected" : material.TexturePayload is null ? "none" : material.TexturePayload.Identity ?? "payload")}).");

        Stopwatch textureImportStopwatch = Stopwatch.StartNew();
        Task<Uri?> albedoTextureTask = Task.FromResult<Uri?>(null);
        Task<Uri?> normalTextureTask = Task.FromResult<Uri?>(null);
        Task<Uri?> heightTextureTask = Task.FromResult<Uri?>(null);
        Task<Uri?> metallicTextureTask = Task.FromResult<Uri?>(null);
        Task<Uri?> emissionTextureTask = Task.FromResult<Uri?>(null);

        if (!suppressAlbedoTexture
            && TryResolveAlbedoTextureImport(
                material,
                preparedTextureImportsByIdentity,
                preparedTerrainTextureImportsByOverlay,
                out ResoniteTextureImport? textureAsset))
        {
            ReportProgress($"[live] Material '{material.MaterialKey}' importing albedo texture.");
            albedoTextureTask = ImportOptionalTextureAsync(client, textureAsset, cancellationToken);
        }
        else if (!suppressAlbedoTexture
            && material.AssetScope == ResoniteMaterialAssetScope.Common
            && !string.IsNullOrWhiteSpace(material.Family))
        {
            string albedoPath = BundledDefaultMaterialAssetStore.GetAbsolutePath(
                BundledDefaultMaterialFamilies.GetVariant(material.Family!, material.BundledVariantIndex ?? 0));
            albedoTextureTask = ImportOptionalTextureAsync(
                client,
                await ResoniteTextureImportFactory.CreateRawFromFileAsync(
                    albedoPath,
                    ResoniteTextureColorProfiles.Srgb,
                    cancellationToken),
                cancellationToken);
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
                    await ResoniteTextureImportFactory.CreateRawFromFileAsync(
                        textureSet.NormalPath,
                        ResoniteTextureColorProfiles.Linear,
                        cancellationToken),
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
                    await ResoniteTextureImportFactory.CreateRawFromFileAsync(
                        textureSet.HeightPath,
                        ResoniteTextureColorProfiles.Linear,
                        cancellationToken),
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
                    await ResoniteTextureImportFactory.CreateRawFromFileAsync(
                        textureSet.MetallicPath,
                        ResoniteTextureColorProfiles.Linear,
                        cancellationToken),
                    cancellationToken);
            }

            if (textureSet.EmissionPath is not null)
            {
                ReportProgress(
                    $"[live] Material '{material.MaterialKey}' importing bundled emission map from '{textureSet.EmissionPath}'.");
                emissionTextureTask = ImportOptionalTextureAsync(
                    client,
                    await ResoniteTextureImportFactory.CreateRawFromFileAsync(
                        textureSet.EmissionPath,
                        ResoniteTextureColorProfiles.Srgb,
                        cancellationToken),
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
        CreatedSlot createdMaterialSlot = await getOrCreateSharedChildSlotAsync(
            client,
            materialContainerParentId,
            materialSlotName,
            cancellationToken);
        materialContainerSlotId = createdMaterialSlot.SlotId;

        Stopwatch componentCreateStopwatch = Stopwatch.StartNew();
        if (albedoTextureUri is not null)
        {
            CreatedComponent albedoTexture = await CreateTextureComponentFromImportedUriAsync(
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
            CreatedComponent normalTexture = await CreateTextureComponentFromImportedUriAsync(
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
            CreatedComponent heightTexture = await CreateTextureComponentFromImportedUriAsync(
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
            CreatedComponent metallicTexture = await CreateTextureComponentFromImportedUriAsync(
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
            CreatedComponent emissionTexture = await CreateTextureComponentFromImportedUriAsync(
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
        CreatedComponent materialComponent = await createComponentAsync(
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
                + "lookup_s=0.000 "
                + $"texture_imports_s={textureImportStopwatch.Elapsed.TotalSeconds:F3} "
                + $"component_create_s={componentCreateStopwatch.Elapsed.TotalSeconds:F3} "
                + $"total_s={totalStopwatch.Elapsed.TotalSeconds:F3}."));
        ReportProgress($"[live] Material '{material.MaterialKey}' ready.");
        return materialComponent;
    }

    private Task<CreatedComponent> CreateTextureComponentFromImportedUriAsync(
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

    private static bool TryResolveAlbedoTextureImport(
        ResoniteMaterialBinding material,
        IReadOnlyDictionary<string, ResoniteTextureImport> preparedTextureImportsByIdentity,
        IReadOnlyDictionary<TerrainTextureOverlay, ResoniteTextureImport> preparedTerrainTextureImportsByOverlay,
        out ResoniteTextureImport textureImport)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(preparedTextureImportsByIdentity);
        ArgumentNullException.ThrowIfNull(preparedTerrainTextureImportsByOverlay);

        if (material.TexturePayload is not null
            && !string.IsNullOrWhiteSpace(material.TexturePayload.Identity)
            && preparedTextureImportsByIdentity.TryGetValue(material.TexturePayload.Identity, out ResoniteTextureImport? preparedPayloadImport))
        {
            textureImport = preparedPayloadImport;
            return true;
        }

        if (material.TerrainOverlay is not null
            && preparedTerrainTextureImportsByOverlay.TryGetValue(
                material.TerrainOverlay,
                out ResoniteTextureImport? preparedTerrainTextureImport))
        {
            textureImport = preparedTerrainTextureImport;
            return true;
        }

        textureImport = null!;
        return false;
    }

    private void ReportProgress(string message)
    {
        progressReporter?.Invoke(message);
    }

}

internal sealed record CreatedMaterialAsset(
    string MaterialComponentId,
    string? MaterialPropertyBlockComponentId);
