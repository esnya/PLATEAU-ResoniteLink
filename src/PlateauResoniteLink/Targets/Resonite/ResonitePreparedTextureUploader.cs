using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResonitePreparedTextureUploader
{
    Task<UploadedTextureAssetSet> UploadAsync(
        LiveSendRunState state,
        IResoniteLinkClient importClient,
        PreparedCityObject preparedCityObject,
        Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay,
        CancellationToken cancellationToken);
}

internal sealed class ResonitePreparedTextureUploader : IResonitePreparedTextureUploader
{
    private readonly IResoniteSharedTerrainTextureAssetStore sharedTerrainTextureAssets;

    public ResonitePreparedTextureUploader(IResoniteSharedTerrainTextureAssetStore sharedTerrainTextureAssets)
    {
        this.sharedTerrainTextureAssets =
            sharedTerrainTextureAssets ?? throw new ArgumentNullException(nameof(sharedTerrainTextureAssets));
    }

    public async Task<UploadedTextureAssetSet> UploadAsync(
        LiveSendRunState state,
        IResoniteLinkClient importClient,
        PreparedCityObject preparedCityObject,
        Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(importClient);
        ArgumentNullException.ThrowIfNull(preparedCityObject);
        ArgumentNullException.ThrowIfNull(preparedTerrainTextureDataByOverlay);

        Dictionary<ResoniteTexturePayload, Uri> textureUrisByPayload = new(TexturePayloadReferenceComparer.Instance);
        Dictionary<TerrainTextureOverlay, Uri> terrainTextureUrisByOverlay = [];
        Dictionary<string, ResoniteComponentLocator> terrainTexturePropertyBlockComponentsByMeshCode = new(StringComparer.Ordinal);
        HashSet<ResoniteTexturePayload> queuedPayloads = new(TexturePayloadReferenceComparer.Instance);
        List<(PreparedTextureReference Texture, Task<Uri> ImportTask)> textureImportTasks = [];
        List<(PreparedTextureReference Texture, Task<SharedTerrainTextureAsset> ImportTask)> terrainTextureImportTasks = [];

        foreach (PreparedTextureReference texture in preparedCityObject.Textures)
        {
            if (texture.TexturePayload is not null && !queuedPayloads.Add(texture.TexturePayload))
            {
                continue;
            }

            if (texture is { TerrainOverlay: not null, GeneratedTerrainTexture: not null })
            {
                string meshCode = ResolveTerrainTextureMeshCode(texture);
                terrainTextureImportTasks.Add((
                    texture,
                    sharedTerrainTextureAssets.EnsureAsync(
                        state,
                        importClient,
                        meshCode,
                        texture.TextureImport,
                        cancellationToken)));
                continue;
            }

            textureImportTasks.Add((
                texture,
                importClient.ImportTextureAsync(texture.TextureImport, cancellationToken)));
        }

        await Task.WhenAll(textureImportTasks.Select(static textureImport => textureImport.ImportTask));
        await Task.WhenAll(terrainTextureImportTasks.Select(static textureImport => textureImport.ImportTask));

        foreach ((PreparedTextureReference texture, Task<Uri> importTask) in textureImportTasks)
        {
            Uri textureUri = await importTask;
            if (texture.TexturePayload is not null)
            {
                textureUrisByPayload.Add(texture.TexturePayload, textureUri);
            }
        }

        foreach ((PreparedTextureReference texture, Task<SharedTerrainTextureAsset> importTask) in terrainTextureImportTasks)
        {
            SharedTerrainTextureAsset sharedTexture = await importTask;
            string meshCode = ResolveTerrainTextureMeshCode(texture);
            terrainTextureUrisByOverlay.Add(texture.TerrainOverlay!, sharedTexture.TextureUri);
            terrainTexturePropertyBlockComponentsByMeshCode.TryAdd(meshCode, sharedTexture.MainTexturePropertyBlockComponent.Locator);
        }

        return new UploadedTextureAssetSet(
            textureUrisByPayload,
            terrainTextureUrisByOverlay,
            terrainTexturePropertyBlockComponentsByMeshCode,
            preparedTerrainTextureDataByOverlay);
    }

    private static string ResolveTerrainTextureMeshCode(PreparedTextureReference texture)
    {
        if (texture.TerrainMeshCode is not { Length: > 0 } meshCode
            || meshCode.Length != 8
            || !PlateauMeshCode.TryGetBounds(meshCode, out _))
        {
            throw new InvalidOperationException(
                "Terrain texture overlay preparation requires a valid third-level mesh code. "
                + $"provided_mesh='{texture.TerrainMeshCode ?? "<null>"}'.");
        }

        return meshCode;
    }

    private sealed class TexturePayloadReferenceComparer : IEqualityComparer<ResoniteTexturePayload>
    {
        internal static readonly TexturePayloadReferenceComparer Instance = new();

        public bool Equals(ResoniteTexturePayload? x, ResoniteTexturePayload? y) => ReferenceEquals(x, y);

        public int GetHashCode(ResoniteTexturePayload obj) => RuntimeHelpers.GetHashCode(obj);
    }
}

internal sealed record UploadedTextureAssetSet(
    Dictionary<ResoniteTexturePayload, Uri> TextureUrisByPayload,
    Dictionary<TerrainTextureOverlay, Uri> TerrainTextureUrisByOverlay,
    Dictionary<string, ResoniteComponentLocator> TerrainTexturePropertyBlockComponentsByMeshCode,
    Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> GeneratedTerrainTexturesByOverlay);
