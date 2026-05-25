using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteSharedTerrainTextureAssetWriter
{
    Task<SharedTerrainTextureAsset> EnsureAsync(
        LiveSendRunState state,
        IResoniteLinkClient importClient,
        string meshCode,
        ResoniteTextureImport textureImport,
        CancellationToken cancellationToken);
}

internal sealed class ResoniteSharedTerrainTextureAssetWriter : IResoniteSharedTerrainTextureAssetWriter
{
    public Task<SharedTerrainTextureAsset> EnsureAsync(
        LiveSendRunState state,
        IResoniteLinkClient importClient,
        string meshCode,
        ResoniteTextureImport textureImport,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(importClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(meshCode);
        ArgumentNullException.ThrowIfNull(textureImport);

        return state.TerrainTextures.AssetsByMeshCode.GetOrCreateAsync(
            meshCode,
            () => EnsureCoreAsync(state, importClient, meshCode, textureImport, cancellationToken),
            cancellationToken);
    }

    private static async Task<SharedTerrainTextureAsset> EnsureCoreAsync(
        LiveSendRunState state,
        IResoniteLinkClient importClient,
        string meshCode,
        ResoniteTextureImport textureImport,
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
            meshCode,
            cancellationToken);
        SharedTerrainTextureAsset? existingTexture = await TryFindSharedTerrainTextureAssetAsync(
            importClient,
            meshSlot,
            cancellationToken);
        if (existingTexture is not null)
        {
            Uri refreshedTextureUri = await importClient.ImportTextureAsync(textureImport, cancellationToken);
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

        Uri importedTextureUri = await importClient.ImportTextureAsync(textureImport, cancellationToken);
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
