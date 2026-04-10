using System.Collections.Concurrent;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Cli;

internal sealed class ResoniteTextureImportResolver
{
    private readonly IPlateauDatasetContentSource datasetContentSource;
    private readonly string generatedAssetsRoot;
    private readonly ITerrainTextureAssetGenerator terrainTextureAssetGenerator;
    private readonly TerrainTextureOverlayLookup terrainTextureOverlayLookup;
    private readonly ConcurrentDictionary<TextureReferenceKey, Lazy<Task<ResoniteTextureImport>>> resolvedTexturePathTasks = [];

    public ResoniteTextureImportResolver(
        IPlateauDatasetContentSource datasetContentSource,
        string generatedAssetsRoot,
        IEnumerable<TerrainTextureOverlay> terrainTextureOverlays,
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
    {
        ArgumentNullException.ThrowIfNull(datasetContentSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(generatedAssetsRoot);
        ArgumentNullException.ThrowIfNull(terrainTextureOverlays);
        ArgumentNullException.ThrowIfNull(terrainTextureAssetGenerator);

        this.datasetContentSource = datasetContentSource;
        this.generatedAssetsRoot = generatedAssetsRoot;
        this.terrainTextureAssetGenerator = terrainTextureAssetGenerator;
        terrainTextureOverlayLookup = new TerrainTextureOverlayLookup(terrainTextureOverlays);
    }

    public Task<ResoniteTextureImport> ResolveAsync(
        string texturePath,
        ResoniteTextureSourceKind textureSourceKind,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(texturePath);

        TextureReferenceKey cacheKey = ResoniteMaterialAssetManager.CreateTextureReferenceKey(texturePath, textureSourceKind);
        Lazy<Task<ResoniteTextureImport>> resolvedTexture = resolvedTexturePathTasks.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<ResoniteTextureImport>>(
                () => ResolveCoreAsync(texturePath, textureSourceKind, CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication));

        Task<ResoniteTextureImport> sharedTask = resolvedTexture.Value;
        return AwaitResolvedTextureAsync(cacheKey, resolvedTexture, sharedTask, cancellationToken);
    }

    private async Task<ResoniteTextureImport> AwaitResolvedTextureAsync(
        TextureReferenceKey cacheKey,
        Lazy<Task<ResoniteTextureImport>> resolvedTexture,
        Task<ResoniteTextureImport> sharedTask,
        CancellationToken cancellationToken)
    {
        try
        {
            return await sharedTask.WaitAsync(cancellationToken);
        }
        catch
        {
            if (sharedTask.IsFaulted || sharedTask.IsCanceled)
            {
                resolvedTexturePathTasks.TryRemove(
                    new KeyValuePair<TextureReferenceKey, Lazy<Task<ResoniteTextureImport>>>(cacheKey, resolvedTexture));
            }

            throw;
        }
    }

    private async Task<ResoniteTextureImport> ResolveCoreAsync(
        string texturePath,
        ResoniteTextureSourceKind textureSourceKind,
        CancellationToken cancellationToken)
    {
        if (terrainTextureOverlayLookup.TryGetOverlay(texturePath, out TerrainTextureOverlay? terrainTextureOverlay))
        {
            return await terrainTextureAssetGenerator.EnsureTextureAsync(
                terrainTextureOverlay!,
                cancellationToken);
        }

        string absoluteTexturePath = textureSourceKind switch
        {
            ResoniteTextureSourceKind.Dataset => await datasetContentSource.MaterializeFileAsync(
                texturePath,
                generatedAssetsRoot,
                cancellationToken),
            ResoniteTextureSourceKind.Bundled => BundledDefaultMaterialAssetStore.GetAbsolutePath(texturePath),
            _ => throw new InvalidOperationException($"Unsupported texture source kind '{textureSourceKind}'."),
        };

        return ResoniteTextureImportFactory.CreateFromFile(absoluteTexturePath);
    }
}
