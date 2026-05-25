using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record ResoniteUploadedTextureAssetSet(
    Dictionary<ResoniteTexturePayload, Uri> TextureUrisByPayload,
    Dictionary<TerrainTextureOverlay, Uri> TerrainTextureUrisByOverlay,
    Dictionary<string, ResoniteComponentLocator> TerrainTexturePropertyBlockComponentsByMeshCode);

internal interface IResonitePreparedTextureUploader
{
    Task<ResoniteUploadedTextureAssetSet> UploadAsync(
        LiveSendRunState state,
        IResoniteLinkClient importClient,
        PreparedCityObject preparedCityObject,
        CancellationToken cancellationToken);
}

internal sealed class ResonitePreparedTextureUploader(
    IResoniteSharedTerrainTextureAssetWriter terrainTextureAssetWriter) : IResonitePreparedTextureUploader
{
    private readonly IResoniteSharedTerrainTextureAssetWriter terrainTextureAssetWriter =
        terrainTextureAssetWriter ?? throw new ArgumentNullException(nameof(terrainTextureAssetWriter));

    public async Task<ResoniteUploadedTextureAssetSet> UploadAsync(
        LiveSendRunState state,
        IResoniteLinkClient importClient,
        PreparedCityObject preparedCityObject,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(importClient);
        ArgumentNullException.ThrowIfNull(preparedCityObject);

        Dictionary<ResoniteTexturePayload, Uri> textureUrisByPayload = new(ResoniteTexturePayloadReferenceComparer.Instance);
        Dictionary<TerrainTextureOverlay, Uri> terrainTextureUrisByOverlay = [];
        Dictionary<string, ResoniteComponentLocator> terrainTexturePropertyBlockComponentsByMeshCode = new(StringComparer.Ordinal);
        HashSet<ResoniteTexturePayload> queuedPayloads = new(ResoniteTexturePayloadReferenceComparer.Instance);
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
                    terrainTextureAssetWriter.EnsureAsync(
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

        Task[] importTasks = textureImportTasks
            .Select(static textureImport => (Task)textureImport.ImportTask)
            .Concat(terrainTextureImportTasks.Select(static textureImport => (Task)textureImport.ImportTask))
            .ToArray();
        await Task.WhenAll(importTasks);

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

        return new ResoniteUploadedTextureAssetSet(
            textureUrisByPayload,
            terrainTextureUrisByOverlay,
            terrainTexturePropertyBlockComponentsByMeshCode);
    }

    private static string ResolveTerrainTextureMeshCode(PreparedTextureReference texture)
    {
        if (texture.TerrainMeshCode is not { Length: > 0 } meshCode
            || meshCode.Length != 8
            || !PlateauMeshCode.TryGetBounds(meshCode, out _))
        {
            throw new InvalidOperationException(
                "Terrain texture overlay preparation requires a valid third-level mesh-code. "
                + $"provided_mesh='{texture.TerrainMeshCode ?? "<null>"}'.");
        }

        return meshCode;
    }
}
