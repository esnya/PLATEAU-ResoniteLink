using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Cli;

internal sealed class ResoniteTextureImportResolver
{
    private readonly IPlateauDatasetContentSource datasetContentSource;
    private readonly string generatedAssetsRoot;
    private readonly ITerrainTextureAssetGenerator terrainTextureAssetGenerator;
    private readonly TerrainTextureOverlayLookup terrainTextureOverlayLookup;
    private readonly ConcurrentDictionary<string, Task<ResolvedTextureImport>> resolvedTexturePathTasks = new(StringComparer.OrdinalIgnoreCase);

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

    public Task<ResolvedTextureImport> ResolveAsync(
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

    public static string ComputeFingerprint(ResoniteTextureImport textureImport)
    {
        return textureImport switch
        {
            ResoniteFileTextureImport fileTextureImport => ComputeContentFingerprint(fileTextureImport.AbsolutePath),
            ResoniteRawTextureImport rawTextureImport => CreateRawTextureFingerprint(rawTextureImport),
            ResoniteRawHdrTextureImport rawHdrTextureImport => CreateRawHdrTextureFingerprint(rawHdrTextureImport),
            _ => throw new InvalidOperationException($"Unsupported texture import type '{textureImport.GetType().Name}'."),
        };
    }

    private async Task<ResolvedTextureImport> ResolveCoreAsync(
        string texturePath,
        ResoniteTextureSourceKind textureSourceKind,
        CancellationToken cancellationToken)
    {
        if (terrainTextureOverlayLookup.TryGetOverlay(texturePath, out TerrainTextureOverlay? terrainTextureOverlay))
        {
            ResoniteRawTextureImport overlayTextureImport = await terrainTextureAssetGenerator.EnsureTextureAsync(
                terrainTextureOverlay!,
                cancellationToken);
            return new ResolvedTextureImport(
                overlayTextureImport,
                ComputeFingerprint(overlayTextureImport));
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

        return new ResolvedTextureImport(
            ResoniteTextureImportFactory.CreateFromFile(absoluteTexturePath),
            ComputeContentFingerprint(absoluteTexturePath));
    }

    private static string CreateRawTextureFingerprint(ResoniteRawTextureImport textureImport)
    {
        using IncrementalHash incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        incrementalHash.AppendData(BitConverter.GetBytes(textureImport.Width));
        incrementalHash.AppendData(BitConverter.GetBytes(textureImport.Height));
        incrementalHash.AppendData(Encoding.UTF8.GetBytes(textureImport.ColorProfile));
        incrementalHash.AppendData(textureImport.RawRgba32Bytes);
        return Convert.ToHexString(incrementalHash.GetHashAndReset());
    }

    private static string CreateRawHdrTextureFingerprint(ResoniteRawHdrTextureImport textureImport)
    {
        using IncrementalHash incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        incrementalHash.AppendData(BitConverter.GetBytes(textureImport.Width));
        incrementalHash.AppendData(BitConverter.GetBytes(textureImport.Height));
        incrementalHash.AppendData(textureImport.RawRgbaFloatBytes);
        return Convert.ToHexString(incrementalHash.GetHashAndReset());
    }

    private static string ComputeContentFingerprint(string absolutePath)
    {
        using IncrementalHash incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using FileStream fileStream = new(
            absolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            useAsync: false);
        byte[] buffer = new byte[16 * 1024];
        int bytesRead;

        while ((bytesRead = fileStream.Read(buffer)) > 0)
        {
            incrementalHash.AppendData(buffer, 0, bytesRead);
        }

        return Convert.ToHexString(incrementalHash.GetHashAndReset());
    }
}

internal sealed record ResolvedTextureImport(
    ResoniteTextureImport TextureImport,
    string SourceFingerprint);
