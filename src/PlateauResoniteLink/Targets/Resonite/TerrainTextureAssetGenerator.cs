using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface ITerrainTextureAssetGenerator
{
    Task<GeneratedTerrainTexture> EnsureTextureAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken);
}

internal sealed record GeneratedTerrainTexture(
    ResoniteRawTextureImport TextureImport,
    TextureUvRect OccupiedUvRect,
    TerrainTextureSource? UsedSource = null,
    IReadOnlyList<TerrainTextureSource>? UsedSources = null)
{
    public GeneratedTerrainTexture(
        ResoniteRawTextureImport textureImport,
        ResoniteFloat2 canvasScale,
        ResoniteFloat2 canvasOffset,
        TerrainTextureSource? usedSource = null,
        IReadOnlyList<TerrainTextureSource>? usedSources = null)
        : this(
            textureImport,
            TextureUvRect.FromScaleOffsetValue(
                new ScalarPair(canvasScale.X, canvasScale.Y),
                new ScalarPair(canvasOffset.X, canvasOffset.Y)),
            usedSource,
            usedSources)
    {
    }
}

internal sealed class TerrainTextureAssetGenerator(
    ITerrainTextureTileImageLoader tileImageLoader) : ITerrainTextureAssetGenerator
{
    // Approximate dry brown soil tone (Munsell 10YR 5/3 family) for uncovered DEM texels.
    internal static readonly Rgba32 DefaultDemGroundFillColor = TerrainTextureImageComposer.DefaultGroundFillColor;

    private readonly ITerrainTextureTileImageLoader tileImageLoader = tileImageLoader ?? throw new ArgumentNullException(nameof(tileImageLoader));
    private readonly AsyncCompletedResultCache<TerrainTextureOverlay, CachedTerrainTexture> cachedTextures = new();

    public async Task<GeneratedTerrainTexture> EnsureTextureAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureOverlay);

        CachedTerrainTexture cachedTexture = await cachedTextures.GetOrCreateAsync(
            terrainTextureOverlay,
            ct => CreateTextureAsync(terrainTextureOverlay, ct),
            cancellationToken);
        return cachedTexture.GeneratedTexture;
    }

    private async Task<CachedTerrainTexture> CreateTextureAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken)
    {
        Image<Rgba32>? composedTexture = null;
        TextureUvRect? composedOccupiedUvRect = null;
        List<TerrainTextureSource> usedSources = [];
        TerrainTextureSource? usedSource = null;

        for (int sourceIndex = 0; sourceIndex < terrainTextureOverlay.Sources.Count; sourceIndex++)
        {
            TerrainTextureSource terrainTextureSource = terrainTextureOverlay.Sources[sourceIndex];
            TerrainTextureSourceImage? sourceImage = terrainTextureSource switch
            {
                TerrainTextureTileSource tileSource => await TryCreateTextureFromTileSourceAsync(
                    terrainTextureOverlay,
                    tileSource,
                    cancellationToken),
                TerrainTextureGeoReferencedRasterSource rasterSource => await TryCreateTextureFromGeoReferencedRasterSourceAsync(
                    terrainTextureOverlay,
                    rasterSource,
                    cancellationToken),
                _ => null,
            };
            if (sourceImage is null)
            {
                continue;
            }

            using (sourceImage)
            {
                Image<Rgba32> image = sourceImage.Image;
                if (!TerrainTextureImageComposer.HasRenderablePixels(image))
                {
                    continue;
                }

                if (composedTexture is null)
                {
                    composedTexture = image.Clone();
                    composedOccupiedUvRect = sourceImage.OccupiedUvRect;
                    usedSource = terrainTextureSource;
                    usedSources.Add(terrainTextureSource);
                }
                else
                {
                    using Image<Rgba32> resizedImage = TerrainTextureImageComposer.ResizeSourceImage(image, composedTexture.Width, composedTexture.Height);
                    if (TerrainTextureImageComposer.FillTransparentPixels(composedTexture, resizedImage))
                    {
                        usedSource = terrainTextureSource;
                        usedSources.Add(terrainTextureSource);
                    }
                }
                if (!TerrainTextureImageComposer.HasTransparentPixels(composedTexture))
                {
                    break;
                }
            }
        }

        if (composedTexture is null)
        {
            throw new HttpRequestException(
                $"Terrain texture generation failed for sources [{DescribeTerrainTextureSources(terrainTextureOverlay.Sources)}].");
        }

        using (composedTexture)
        {
            TerrainTextureSource terrainTextureSource = usedSource ?? terrainTextureOverlay.PrimarySource;
            GeneratedTerrainTexture generatedTexture = TerrainTextureImageComposer.CreateGeneratedTexture(
                composedTexture,
                terrainTextureOverlay.MaxTextureSize,
                terrainTextureSource,
                usedSources,
                composedOccupiedUvRect);
            return new CachedTerrainTexture(generatedTexture, terrainTextureSource);
        }
    }

    private static ExpandedTileCrop CreateExpandedTileCrop(
        TerrainTextureLayoutPlan layoutPlan,
        int zoomLevel,
        int maxTextureSize)
    {
        int canvasWidth = TerrainTextureImageComposer.RoundUpToPowerOfTwo(layoutPlan.CropWidth);
        int canvasHeight = TerrainTextureImageComposer.RoundUpToPowerOfTwo(layoutPlan.CropHeight);
        if (canvasWidth > maxTextureSize || canvasHeight > maxTextureSize)
        {
            return ExpandedTileCrop.FromLayout(layoutPlan);
        }

        int occupiedLeft = (canvasWidth - layoutPlan.CropWidth) / 2;
        int occupiedTop = (canvasHeight - layoutPlan.CropHeight) / 2;
        int layoutGlobalLeft = (layoutPlan.MinTileX * WebMercatorTileMath.TileSizePixels) + layoutPlan.CropLeft;
        int layoutGlobalTop = (layoutPlan.MinTileY * WebMercatorTileMath.TileSizePixels) + layoutPlan.CropTop;
        int expandedGlobalLeft = layoutGlobalLeft - occupiedLeft;
        int expandedGlobalTop = layoutGlobalTop - occupiedTop;
        int expandedGlobalRight = expandedGlobalLeft + canvasWidth;
        int expandedGlobalBottom = expandedGlobalTop + canvasHeight;
        int maxTileIndex = (1 << zoomLevel) - 1;
        int minTileX = Math.Clamp((int)Math.Floor(expandedGlobalLeft / (double)WebMercatorTileMath.TileSizePixels), 0, maxTileIndex);
        int maxTileX = Math.Clamp((int)Math.Floor((expandedGlobalRight - 1) / (double)WebMercatorTileMath.TileSizePixels), 0, maxTileIndex);
        int minTileY = Math.Clamp((int)Math.Floor(expandedGlobalTop / (double)WebMercatorTileMath.TileSizePixels), 0, maxTileIndex);
        int maxTileY = Math.Clamp((int)Math.Floor((expandedGlobalBottom - 1) / (double)WebMercatorTileMath.TileSizePixels), 0, maxTileIndex);
        int stitchedWidth = (maxTileX - minTileX + 1) * WebMercatorTileMath.TileSizePixels;
        int stitchedHeight = (maxTileY - minTileY + 1) * WebMercatorTileMath.TileSizePixels;
        int cropLeft = Math.Clamp(expandedGlobalLeft - (minTileX * WebMercatorTileMath.TileSizePixels), 0, Math.Max(0, stitchedWidth - canvasWidth));
        int cropTop = Math.Clamp(expandedGlobalTop - (minTileY * WebMercatorTileMath.TileSizePixels), 0, Math.Max(0, stitchedHeight - canvasHeight));
        int actualCropWidth = Math.Min(canvasWidth, stitchedWidth - cropLeft);
        int actualCropHeight = Math.Min(canvasHeight, stitchedHeight - cropTop);
        if (actualCropWidth <= 0 || actualCropHeight <= 0)
        {
            return ExpandedTileCrop.FromLayout(layoutPlan);
        }

        int occupiedX = Math.Clamp(
            layoutGlobalLeft - ((minTileX * WebMercatorTileMath.TileSizePixels) + cropLeft),
            0,
            actualCropWidth - 1);
        int occupiedY = Math.Clamp(
            layoutGlobalTop - ((minTileY * WebMercatorTileMath.TileSizePixels) + cropTop),
            0,
            actualCropHeight - 1);
        TextureUvRect occupiedUvRect = TextureUvRect.FromTopLeftPixelRect(
            occupiedX,
            occupiedY,
            Math.Min(layoutPlan.CropWidth, actualCropWidth - occupiedX),
            Math.Min(layoutPlan.CropHeight, actualCropHeight - occupiedY),
            actualCropWidth,
            actualCropHeight);
        return new ExpandedTileCrop(
            minTileX,
            maxTileX,
            minTileY,
            maxTileY,
            stitchedWidth,
            stitchedHeight,
            cropLeft,
            cropTop,
            actualCropWidth,
            actualCropHeight,
            occupiedUvRect);
    }

    private async Task<TerrainTextureSourceImage?> TryCreateTextureFromTileSourceAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        TerrainTextureTileSource tileSource,
        CancellationToken cancellationToken)
    {
        TerrainTextureLayoutPlan layoutPlan = TerrainTextureLayoutPlanner.Create(
            terrainTextureOverlay.GeographicBounds,
            tileSource.ZoomLevel);
        ExpandedTileCrop tileCrop = CreateExpandedTileCrop(layoutPlan, tileSource.ZoomLevel, terrainTextureOverlay.MaxTextureSize);
        using Image<Rgba32> stitchedImage = new(tileCrop.StitchedWidth, tileCrop.StitchedHeight);
        bool anyTileRendered = false;
        for (int tileY = tileCrop.MinTileY; tileY <= tileCrop.MaxTileY; tileY++)
        {
            for (int tileX = tileCrop.MinTileX; tileX <= tileCrop.MaxTileX; tileX++)
            {
                Image<Rgba32>? tileImage = await tileImageLoader.TryLoadAsync(
                    tileSource,
                    tileX,
                    tileY,
                    cancellationToken);
                if (tileImage is null)
                {
                    continue;
                }

                using (tileImage)
                {
                    anyTileRendered = true;
                    stitchedImage.Mutate(context => context.DrawImage(
                        tileImage,
                        new Point(
                            (tileX - tileCrop.MinTileX) * WebMercatorTileMath.TileSizePixels,
                            (tileY - tileCrop.MinTileY) * WebMercatorTileMath.TileSizePixels),
                        1.0f));
                }
            }
        }

        if (!anyTileRendered)
        {
            return null;
        }

        return new TerrainTextureSourceImage(
            stitchedImage.Clone(context => context.Crop(new Rectangle(
                tileCrop.CropLeft,
                tileCrop.CropTop,
                tileCrop.CropWidth,
                tileCrop.CropHeight))),
            tileCrop.OccupiedUvRect);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The returned source image owns and disposes the cropped raster image.")]
    private static async Task<TerrainTextureSourceImage?> TryCreateTextureFromGeoReferencedRasterSourceAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        TerrainTextureGeoReferencedRasterSource rasterSource,
        CancellationToken cancellationToken)
    {
        string sourcePath = Path.GetFullPath(rasterSource.SourcePath);
        GeoReferencedRasterMetadata? metadata = rasterSource.Metadata
            ?? await TerrainTextureGeoReferencedRasterMetadataReader.TryReadMetadataAsync(sourcePath, cancellationToken);
        if (metadata is null || !metadata.IsUsable)
        {
            return null;
        }

        try
        {
            using Image<Rgba32> sourceImage = await Image.LoadAsync<Rgba32>(sourcePath, cancellationToken);
            Image<Rgba32>? cropped = TerrainTextureGeoReferencedRasterCropper.TryCrop(
                sourceImage,
                metadata,
                terrainTextureOverlay.GeographicBounds);
            return cropped is null
                ? null
                : new TerrainTextureSourceImage(cropped, null);
        }
        catch (UnknownImageFormatException)
        {
            return null;
        }
        catch (InvalidImageContentException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string DescribeTerrainTextureSources(IEnumerable<TerrainTextureSource> sources)
    {
        return string.Join(
            ", ",
            sources.Select(static source => source switch
            {
                TerrainTextureTileSource tileSource => $"tile:{tileSource.ZoomLevel}:{tileSource.UrlTemplate}",
                TerrainTextureGeoReferencedRasterSource rasterSource => $"georaster:{rasterSource.SourcePath}",
                _ => source.GetType().Name,
            }));
    }

    private sealed record CachedTerrainTexture(
        GeneratedTerrainTexture GeneratedTexture,
        TerrainTextureSource UsedSource);

    private sealed record TerrainTextureSourceImage(
        Image<Rgba32> Image,
        TextureUvRect? OccupiedUvRect) : IDisposable
    {
        public void Dispose()
        {
            Image.Dispose();
        }
    }

    private sealed record ExpandedTileCrop(
        int MinTileX,
        int MaxTileX,
        int MinTileY,
        int MaxTileY,
        int StitchedWidth,
        int StitchedHeight,
        int CropLeft,
        int CropTop,
        int CropWidth,
        int CropHeight,
        TextureUvRect? OccupiedUvRect)
    {
        public static ExpandedTileCrop FromLayout(TerrainTextureLayoutPlan layoutPlan)
        {
            return new ExpandedTileCrop(
                layoutPlan.MinTileX,
                layoutPlan.MaxTileX,
                layoutPlan.MinTileY,
                layoutPlan.MaxTileY,
                layoutPlan.StitchedWidth,
                layoutPlan.StitchedHeight,
                layoutPlan.CropLeft,
                layoutPlan.CropTop,
                layoutPlan.CropWidth,
                layoutPlan.CropHeight,
                null);
        }
    }

}
