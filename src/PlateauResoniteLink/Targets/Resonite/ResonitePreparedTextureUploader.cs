using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record ResoniteUploadedTextureAssetSet(
    Dictionary<ResoniteTexturePayload, Uri> TextureUrisByPayload,
    Dictionary<TerrainTextureOverlay, Uri> TerrainTextureUrisByOverlay,
    Dictionary<ThirdRegionalMeshCode, ResoniteComponentLocator> TerrainTexturePropertyBlockComponentsByMeshCode);

internal static class ResonitePreparedTextureUploader
{
    public static async Task<ResoniteUploadedTextureAssetSet> UploadAsync(
        LiveSendRunState state,
        IResoniteLinkClient importClient,
        PreparedCityObject preparedCityObject,
        CancellationToken cancellationToken)
    {
        Dictionary<ResoniteTexturePayload, Uri> textureUrisByPayload = new(ReferenceEqualityComparer.Instance);
        Dictionary<TerrainTextureOverlay, Uri> terrainTextureUrisByOverlay = [];
        Dictionary<ThirdRegionalMeshCode, ResoniteComponentLocator> terrainTexturePropertyBlockComponentsByMeshCode = [];
        HashSet<ResoniteTexturePayload> queuedPayloads = new(ReferenceEqualityComparer.Instance);
        List<(PreparedMaterialTextureReference Texture, Task<Uri> ImportTask)> textureImportTasks = [];
        List<(PreparedTerrainOverlayTextureReference Texture, Task<SharedTerrainTextureAsset> ImportTask)> terrainTextureImportTasks = [];

        foreach (PreparedTextureReference texture in preparedCityObject.Textures)
        {
            if (texture is PreparedMaterialTextureReference materialTexture)
            {
                if (materialTexture.TexturePayload is not null && !queuedPayloads.Add(materialTexture.TexturePayload))
                {
                    continue;
                }

                textureImportTasks.Add((
                    materialTexture,
                    importClient.ImportTextureAsync(materialTexture.TextureSource, cancellationToken)));
                continue;
            }

            if (texture is PreparedTerrainOverlayTextureReference terrainTexture)
            {
                terrainTextureImportTasks.Add((
                    terrainTexture,
                    EnsureSharedTerrainTextureAssetAsync(
                        state,
                        importClient,
                        terrainTexture.MeshCode,
                        terrainTexture.TextureSource,
                        cancellationToken)));
                continue;
            }
        }

        Task[] importTasks = textureImportTasks
            .Select(static textureImport => (Task)textureImport.ImportTask)
            .Concat(terrainTextureImportTasks.Select(static textureImport => (Task)textureImport.ImportTask))
            .ToArray();
        await Task.WhenAll(importTasks);

        foreach ((PreparedMaterialTextureReference texture, Task<Uri> importTask) in textureImportTasks)
        {
            Uri textureUri = await importTask;
            if (texture.TexturePayload is not null)
            {
                textureUrisByPayload.Add(texture.TexturePayload, textureUri);
            }
        }

        foreach ((PreparedTerrainOverlayTextureReference texture, Task<SharedTerrainTextureAsset> importTask) in terrainTextureImportTasks)
        {
            SharedTerrainTextureAsset sharedTexture = await importTask;
            terrainTextureUrisByOverlay.Add(texture.Overlay, sharedTexture.TextureUri);
            terrainTexturePropertyBlockComponentsByMeshCode.TryAdd(texture.MeshCode, sharedTexture.MainTexturePropertyBlockComponent.Locator);
        }

        return new ResoniteUploadedTextureAssetSet(
            textureUrisByPayload,
            terrainTextureUrisByOverlay,
            terrainTexturePropertyBlockComponentsByMeshCode);
    }

    private static Task<SharedTerrainTextureAsset> EnsureSharedTerrainTextureAssetAsync(
        LiveSendRunState state,
        IResoniteLinkClient importClient,
        ThirdRegionalMeshCode meshCode,
        ITextureImportSource textureSource,
        CancellationToken cancellationToken)
    {
        return state.TerrainTextures.AssetsByMeshCode.GetOrCreateAsync(
            meshCode.Value,
            () => EnsureSharedTerrainTextureAssetCoreAsync(state, importClient, meshCode, textureSource, cancellationToken),
            cancellationToken);
    }

    private static async Task<SharedTerrainTextureAsset> EnsureSharedTerrainTextureAssetCoreAsync(
        LiveSendRunState state,
        IResoniteLinkClient importClient,
        ThirdRegionalMeshCode meshCode,
        ITextureImportSource textureSource,
        CancellationToken cancellationToken)
    {
        CreatedSlot terrainTexturesRoot = await state.Placement.GetOrCreateSharedChildSlotAsync(
            importClient,
            state.Context.DatasetAssetsRootSlot.Locator,
            "Terrain Textures",
            cancellationToken);
        CreatedSlot meshSlot = await state.Placement.GetOrCreateSharedChildSlotAsync(
            importClient,
            terrainTexturesRoot.Locator,
            meshCode.Value,
            cancellationToken);
        SharedTerrainTextureAsset? existingTexture = await TryFindSharedTerrainTextureAssetAsync(
            importClient,
            meshSlot,
            cancellationToken);
        if (existingTexture is not null)
        {
            Uri refreshedTextureUri = await importClient.ImportTextureAsync(textureSource, cancellationToken);
            await importClient.UpdateComponentAsync(
                new ResoniteComponentUpdate
                {
                    Component = new ResoniteTransportComponentLocator(existingTexture.TextureComponent.Locator.Value),
                    Members = ResoniteSceneMaterialConventions.CreateTextureMembers(
                        refreshedTextureUri,
                        ResoniteSceneMaterialConventions.TextureMemberRole.TerrainMainTextureOverride),
                },
                cancellationToken);
            return existingTexture with
            {
                TextureUri = refreshedTextureUri,
            };
        }

        Uri importedTextureUri = await importClient.ImportTextureAsync(textureSource, cancellationToken);
        CreatedComponent textureComponent = await ResoniteMaterialPlanning.CreateComponentAsync(
            importClient,
            meshSlot.Locator,
            "[FrooxEngine]FrooxEngine.StaticTexture2D",
            ResoniteSceneMaterialConventions.CreateTextureMembers(
                importedTextureUri,
                ResoniteSceneMaterialConventions.TextureMemberRole.TerrainMainTextureOverride),
            cancellationToken);
        CreatedComponent propertyBlockComponent = await CreateTerrainMainTexturePropertyBlockAsync(
            importClient,
            meshSlot.Locator,
            textureComponent.Locator,
            cancellationToken);
        return new SharedTerrainTextureAsset(
            importedTextureUri,
            textureComponent,
            propertyBlockComponent);
    }

    private static async Task<SharedTerrainTextureAsset?> TryFindSharedTerrainTextureAssetAsync(
        IResoniteLinkClient importClient,
        CreatedSlot meshSlot,
        CancellationToken cancellationToken)
    {
        Slot? slot = await importClient.GetSlotAsync(
            new ResoniteTransportSlotLocator(meshSlot.Locator.Value),
            depth: 0,
            cancellationToken);
        Component? textureComponent = slot?.Components?
            .FirstOrDefault(IsSharedTerrainTextureComponent);
        if (textureComponent?.ID is null
            || textureComponent.Members["URL"] is not Field_Uri url)
        {
            return null;
        }

        if (url.Value is null)
        {
            return null;
        }

        CreatedComponent createdTextureComponent = new(new ResoniteComponentLocator(textureComponent.ID), textureComponent.ComponentType);
        Component? propertyBlockComponent = slot?.Components?
            .Where(component => IsSharedTerrainMainTexturePropertyBlockComponent(component, textureComponent.ID))
            .OrderBy(static component => component.ID, StringComparer.Ordinal)
            .FirstOrDefault();
        CreatedComponent createdPropertyBlockComponent = propertyBlockComponent?.ID is null
            ? await CreateTerrainMainTexturePropertyBlockAsync(
                importClient,
                meshSlot.Locator,
                createdTextureComponent.Locator,
                cancellationToken)
            : new CreatedComponent(new ResoniteComponentLocator(propertyBlockComponent.ID), propertyBlockComponent.ComponentType);

        return new SharedTerrainTextureAsset(
            url.Value,
            createdTextureComponent,
            createdPropertyBlockComponent);
    }

    private static Task<CreatedComponent> CreateTerrainMainTexturePropertyBlockAsync(
        IResoniteLinkClient importClient,
        ResoniteSlotLocator meshSlot,
        ResoniteComponentLocator textureComponent,
        CancellationToken cancellationToken)
    {
        return ResoniteMaterialPlanning.CreateComponentAsync(
            importClient,
            meshSlot,
            "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock",
            new Dictionary<string, Member>(StringComparer.Ordinal)
            {
                ["Texture"] = new Reference
                {
                    TargetID = textureComponent.Value,
                },
            },
            cancellationToken);
    }

    private static bool IsSharedTerrainMainTexturePropertyBlockComponent(Component component, string textureComponentId)
    {
        return string.Equals(
                component.ComponentType,
                "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock",
                StringComparison.Ordinal)
            && component.Members.TryGetValue("Texture", out Member? textureMember)
            && textureMember is Reference { TargetID: string targetId }
            && string.Equals(targetId, textureComponentId, StringComparison.Ordinal);
    }

    private static bool IsSharedTerrainTextureComponent(Component component)
    {
        return string.Equals(
                component.ComponentType,
                "[FrooxEngine]FrooxEngine.StaticTexture2D",
                StringComparison.Ordinal)
            && component.Members.TryGetValue("URL", out Member? urlMember)
            && urlMember is Field_Uri
            && component.Members.TryGetValue("WrapModeU", out Member? wrapModeUMember)
            && wrapModeUMember is Field_Enum { Value: "Clamp" }
            && component.Members.TryGetValue("WrapModeV", out Member? wrapModeVMember)
            && wrapModeVMember is Field_Enum { Value: "Clamp" };
    }

}
