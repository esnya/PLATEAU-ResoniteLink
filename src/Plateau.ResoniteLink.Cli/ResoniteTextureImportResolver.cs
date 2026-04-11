using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Cli;

internal sealed class ResoniteTextureImportResolver
{
    private readonly IPlateauDatasetContentSource datasetContentSource;
    private readonly string runRoot;
    private readonly ITerrainTextureAssetGenerator terrainTextureAssetGenerator;
    private readonly TerrainTextureOverlayLookup terrainTextureOverlayLookup;
    private readonly AsyncCompletedResultCache<TextureReferenceKey, ResoniteTextureImport> resolvedTextureCache = new();

    public ResoniteTextureImportResolver(
        IPlateauDatasetContentSource datasetContentSource,
        string runRoot,
        IEnumerable<TerrainTextureOverlay> terrainTextureOverlays,
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
    {
        ArgumentNullException.ThrowIfNull(datasetContentSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(runRoot);
        ArgumentNullException.ThrowIfNull(terrainTextureOverlays);
        ArgumentNullException.ThrowIfNull(terrainTextureAssetGenerator);

        this.datasetContentSource = datasetContentSource;
        this.runRoot = runRoot;
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
        return resolvedTextureCache.GetOrCreateAsync(
            cacheKey,
            ct => ResolveCoreAsync(texturePath, textureSourceKind, ct),
            cancellationToken);
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
                runRoot,
                cancellationToken),
            ResoniteTextureSourceKind.Bundled => BundledDefaultMaterialAssetStore.GetAbsolutePath(texturePath),
            _ => throw new InvalidOperationException($"Unsupported texture source kind '{textureSourceKind}'."),
        };

        return ResoniteTextureImportFactory.CreateFromFile(absoluteTexturePath);
    }
}
