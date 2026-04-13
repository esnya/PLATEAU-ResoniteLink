using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Cli;

internal sealed class ResoniteTextureImportResolver
{
    private readonly IPlateauDatasetContentSource datasetContentSource;
    private readonly ResoniteTextureImportRegistry textureImportRegistry;
    private readonly ITerrainTextureAssetGenerator terrainTextureAssetGenerator;
    private readonly TerrainTextureOverlayLookup terrainTextureOverlayLookup;
    private readonly AsyncCompletedResultCache<TextureReferenceKey, ResoniteTextureImport> resolvedTextureCache = new();

    public ResoniteTextureImportResolver(
        IPlateauDatasetContentSource datasetContentSource,
        IEnumerable<TerrainTextureOverlay> terrainTextureOverlays,
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
        : this(
            datasetContentSource,
            new ResoniteTextureImportRegistry(),
            terrainTextureOverlays,
            terrainTextureAssetGenerator)
    {
    }

    public ResoniteTextureImportResolver(
        IPlateauDatasetContentSource datasetContentSource,
        ResoniteTextureImportRegistry textureImportRegistry,
        IEnumerable<TerrainTextureOverlay> terrainTextureOverlays,
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
    {
        ArgumentNullException.ThrowIfNull(datasetContentSource);
        ArgumentNullException.ThrowIfNull(textureImportRegistry);
        ArgumentNullException.ThrowIfNull(terrainTextureOverlays);
        ArgumentNullException.ThrowIfNull(terrainTextureAssetGenerator);

        this.datasetContentSource = datasetContentSource;
        this.textureImportRegistry = textureImportRegistry;
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
        if (textureImportRegistry.TryGet(texturePath, textureSourceKind, out ResoniteTextureImport? registeredTextureImport))
        {
            return registeredTextureImport!;
        }

        if (terrainTextureOverlayLookup.TryGetOverlay(texturePath, out TerrainTextureOverlay? terrainTextureOverlay))
        {
            return await terrainTextureAssetGenerator.EnsureTextureAsync(
                terrainTextureOverlay!,
                cancellationToken);
        }

        return textureSourceKind switch
        {
            ResoniteTextureSourceKind.Dataset => await CreateDatasetRawTextureImportAsync(texturePath, cancellationToken),
            ResoniteTextureSourceKind.Bundled => await ResoniteTextureImportFactory.CreateRawFromFileAsync(
                BundledDefaultMaterialAssetStore.GetAbsolutePath(texturePath),
                ResoniteTextureColorProfiles.Srgb,
                cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported texture source kind '{textureSourceKind}'."),
        };
    }

    private async Task<ResoniteRawTextureImport> CreateDatasetRawTextureImportAsync(
        string texturePath,
        CancellationToken cancellationToken)
    {
        await using Stream textureStream = await datasetContentSource.OpenReadAsync(texturePath, cancellationToken);
        return await ResoniteTextureImportFactory.CreateRawFromStreamAsync(
            textureStream,
            texturePath,
            cancellationToken: cancellationToken);
    }
}
