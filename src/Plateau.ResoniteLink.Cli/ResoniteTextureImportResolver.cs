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
    private readonly ConcurrentDictionary<string, Task<ResoniteTextureImport>> resolvedTexturePathTasks = new(StringComparer.OrdinalIgnoreCase);

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

        string cacheKey = ResoniteMaterialAssetManager.CreateTextureCacheKey(texturePath, textureSourceKind);
        return resolvedTexturePathTasks.GetOrAdd(
            cacheKey,
            _ => ResolveCoreAsync(texturePath, textureSourceKind, cancellationToken));
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
