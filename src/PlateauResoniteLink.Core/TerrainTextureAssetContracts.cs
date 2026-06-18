using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing.Contracts;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Core;

public interface ITerrainTextureAssetGenerator
{
    Task<GeneratedTerrainTexture> EnsureTextureAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken);
}

public interface ITerrainTextureAssetGeneratorFactory
{
    ITerrainTextureAssetGenerator Create(
        HttpClient terrainTextureAssetHttpClient,
        TerrainTextureAssetGeneratorOptions options);
}

public sealed record TerrainTextureAssetGeneratorOptions(
    string? TerrainTileCacheRoot,
    bool DisableTerrainTileCache);

public interface ITextureUvTransformValue
{
    double X { get; }

    double Y { get; }
}

public sealed record GeneratedTerrainTexture
{
    public GeneratedTerrainTexture(
        ITextureImportSource textureSource,
        ITextureUvTransformValue canvasScale,
        ITextureUvTransformValue canvasOffset,
        TerrainTextureSourceUsage usage)
        : this(
            textureSource,
            TextureUvRect.FromScaleOffsetValue(
                new ScalarPair(canvasScale.X, canvasScale.Y),
                new ScalarPair(canvasOffset.X, canvasOffset.Y)),
            CreateSingleUsageSnapshot(usage))
    {
    }

    public GeneratedTerrainTexture(
        ITextureImportSource textureSource,
        ITextureUvTransformValue canvasScale,
        ITextureUvTransformValue canvasOffset,
        IReadOnlyList<TerrainTextureSourceUsage> usages)
        : this(
            textureSource,
            TextureUvRect.FromScaleOffsetValue(
                new ScalarPair(canvasScale.X, canvasScale.Y),
                new ScalarPair(canvasOffset.X, canvasOffset.Y)),
            usages)
    {
    }

    public GeneratedTerrainTexture(
        ITextureImportSource textureSource,
        TextureUvRect occupiedUvRect,
        IReadOnlyList<TerrainTextureSourceUsage> usages)
    {
        ArgumentNullException.ThrowIfNull(textureSource);

        TerrainTextureSourceUsage[] trackedUsages = CreateUsageSnapshot(usages);

        TextureSource = textureSource;
        OccupiedUvRect = occupiedUvRect;
        Usages = Array.AsReadOnly(trackedUsages);
    }

    public ITextureImportSource TextureSource { get; }

    public TextureUvRect OccupiedUvRect { get; }

    public TerrainTextureSourceUsage Usage => Usages.Count == 0
        ? throw new InvalidOperationException("Generated terrain texture has no tracked source.")
        : Usages[0];

    public IReadOnlyList<TerrainTextureSourceUsage> Usages { get; }

    private static TerrainTextureSourceUsage[] CreateSingleUsageSnapshot(TerrainTextureSourceUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        return [usage];
    }

    private static TerrainTextureSourceUsage[] CreateUsageSnapshot(IReadOnlyList<TerrainTextureSourceUsage> usages)
    {
        ArgumentNullException.ThrowIfNull(usages);

        TerrainTextureSourceUsage[] trackedUsages = new TerrainTextureSourceUsage[usages.Count];
        for (int index = 0; index < usages.Count; index++)
        {
            trackedUsages[index] = usages[index]
                ?? throw new ArgumentException("Generated terrain texture usages cannot contain null.", nameof(usages));
        }

        return trackedUsages.Distinct().ToArray();
    }
}

public sealed record TerrainTextureSourceUsage(
    string Key,
    string Description,
    bool RequiresGsiFallbackLicense,
    string TextureImportName)
{
    public static TerrainTextureSourceUsage FromSource(TerrainTextureSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source switch
        {
            TerrainTextureTileSource tileSource => new TerrainTextureSourceUsage(
                Key: tileSource.Description,
                Description: tileSource.Description,
                RequiresGsiFallbackLicense: DemTerrainTextureDefaults.IsGsiFallbackSource(tileSource),
                TextureImportName: nameof(TerrainTextureTileSource)),
            TerrainTextureGeoReferencedRasterSource rasterSource => new TerrainTextureSourceUsage(
                Key: rasterSource.Description,
                Description: rasterSource.Description,
                RequiresGsiFallbackLicense: false,
                TextureImportName: nameof(TerrainTextureGeoReferencedRasterSource)),
            _ => new TerrainTextureSourceUsage(
                Key: source.Description,
                Description: source.Description,
                RequiresGsiFallbackLicense: false,
                TextureImportName: source.GetType().Name),
        };
    }
}
