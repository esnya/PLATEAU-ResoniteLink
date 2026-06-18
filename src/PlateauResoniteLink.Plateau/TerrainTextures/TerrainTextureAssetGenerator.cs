using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using PlateauResoniteLink.Application.Importing.Contracts;

using PlateauResoniteLink.Core;

namespace PlateauResoniteLink.Plateau.TerrainTextures;

internal sealed class TerrainTextureAssetGenerator : ITerrainTextureAssetGenerator
{
    private const string SrgbTextureColorProfile = "sRGB";

    // Approximate dry brown soil tone (Munsell 10YR 5/3 family) for uncovered DEM texels.
    internal static readonly Rgba32 DefaultDemGroundFillColor = new(181, 176, 166, byte.MaxValue);

    private static readonly HttpClient DefaultHttpClient = new();

    private readonly AsyncCompletedResultCache<TerrainTextureOverlay, CachedTerrainTexture> cachedTextures = new();
    private readonly ITerrainTextureSourceImageReader sourceImageReader;

    public TerrainTextureAssetGenerator(
        HttpClient? httpClient = null,
        string? persistentCacheRoot = null,
        bool disablePersistentCache = false)
        : this(new DefaultTerrainTextureSourceImageReader(
            httpClient ?? DefaultHttpClient,
            disablePersistentCache ? null : new PersistentTerrainTileCache(persistentCacheRoot)))
    {
    }

    internal TerrainTextureAssetGenerator(ITerrainTextureSourceImageReader sourceImageReader)
    {
        this.sourceImageReader = sourceImageReader ?? throw new ArgumentNullException(nameof(sourceImageReader));
    }

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

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Each non-null TerrainTextureSourceImage is disposed by the using block before the next source is evaluated.")]
    private async Task<CachedTerrainTexture> CreateTextureAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        CancellationToken cancellationToken)
    {
        Image<Rgba32>? composedTexture = null;
        TextureUvRect? composedOccupiedUvRect = null;
        List<TerrainTextureSourceUsage> usages = [];
        TerrainTextureSourceUsage? primaryUsage = null;

        for (int sourceIndex = 0; sourceIndex < terrainTextureOverlay.Sources.Count; sourceIndex++)
        {
            TerrainTextureSource terrainTextureSource = terrainTextureOverlay.Sources[sourceIndex];
            TerrainTextureSourceReadResult sourceResult = await sourceImageReader.TryReadAsync(
                terrainTextureOverlay,
                terrainTextureSource,
                cancellationToken);
            if (sourceResult.Kind == TerrainTextureSourceReadResultKind.SourceFailure)
            {
                throw new HttpRequestException(sourceResult.FailureMessage);
            }

            TerrainTextureSourceImage? sourceImage = sourceResult.Image;
            if (sourceImage is null)
            {
                continue;
            }

            using (sourceImage)
            {
                Image<Rgba32> image = sourceImage.Image;
                if (!HasRenderablePixels(image))
                {
                    continue;
                }

                if (composedTexture is null)
                {
                    composedTexture = image.Clone();
                    composedOccupiedUvRect = sourceImage.OccupiedUvRect;
                    primaryUsage = sourceResult.Usage
                        ?? throw new InvalidOperationException("Rendered terrain texture source result must include usage.");
                    usages.Add(primaryUsage);
                }
                else
                {
                    using Image<Rgba32> resizedImage = ResizeSourceImage(image, composedTexture.Width, composedTexture.Height);
                    if (FillTransparentPixels(composedTexture, resizedImage))
                    {
                        primaryUsage = sourceResult.Usage
                            ?? throw new InvalidOperationException("Rendered terrain texture source result must include usage.");
                        usages.Add(primaryUsage);
                    }
                }
                if (!HasTransparentPixels(composedTexture))
                {
                    break;
                }
            }
        }

        composedTexture ??= new Image<Rgba32>(1, 1, DefaultDemGroundFillColor);

        using (composedTexture)
        {
            GeneratedTerrainTexture generatedTexture = CreateGeneratedTexture(
                composedTexture,
                terrainTextureOverlay.MaxTextureSize,
                primaryUsage,
                usages,
                composedOccupiedUvRect);
            return new CachedTerrainTexture(generatedTexture);
        }
    }

    private static GeneratedTerrainTexture CreateGeneratedTexture(
        Image<Rgba32> image,
        int maxTextureSize,
        TerrainTextureSourceUsage? primaryUsage,
        List<TerrainTextureSourceUsage> usages,
        TextureUvRect? sourceOccupiedUvRect)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTextureSize);
        using Image<Rgba32> opaqueImage = CreateOpaqueGroundImage(image);

        if (TryCreatePowerOfTwoCanvasTexture(opaqueImage, maxTextureSize, primaryUsage, usages, sourceOccupiedUvRect, out GeneratedTerrainTexture? generatedTexture))
        {
            return generatedTexture!;
        }

        int fallbackMaxTextureSize = TexturePowerOfTwo.RoundDown(maxTextureSize);
        using Image<Rgba32> resizedImage = ResizeToMaxTextureSize(opaqueImage, fallbackMaxTextureSize);
        if (TryCreatePowerOfTwoCanvasTexture(resizedImage, fallbackMaxTextureSize, primaryUsage, usages, null, out generatedTexture))
        {
            return generatedTexture!;
        }

        throw new InvalidOperationException(
            $"Terrain texture fallback failed to fit into a power-of-two canvas within maxTextureSize={maxTextureSize}.");
    }

    private static bool TryCreatePowerOfTwoCanvasTexture(
        Image<Rgba32> image,
        int maxTextureSize,
        TerrainTextureSourceUsage? primaryUsage,
        IReadOnlyList<TerrainTextureSourceUsage> usages,
        TextureUvRect? sourceOccupiedUvRect,
        out GeneratedTerrainTexture? generatedTexture)
    {
        int canvasWidth = TexturePowerOfTwo.RoundUp(image.Width);
        int canvasHeight = TexturePowerOfTwo.RoundUp(image.Height);
        if (canvasWidth > maxTextureSize || canvasHeight > maxTextureSize)
        {
            generatedTexture = null;
            return false;
        }

        using Image<Rgba32> canvasImage = new(canvasWidth, canvasHeight, DefaultDemGroundFillColor);
        int drawOffsetX = (canvasWidth - image.Width) / 2;
        int drawOffsetY = (canvasHeight - image.Height) / 2;
        canvasImage.Mutate(context => context.DrawImage(
            image,
            new Point(drawOffsetX, drawOffsetY),
            1.0f));
        TextureUvRect occupiedUvRect = sourceOccupiedUvRect is { } sourceRect
            ? CreateOccupiedUvRect(
                (int)Math.Round(drawOffsetX + (sourceRect.MinU * image.Width), MidpointRounding.AwayFromZero),
                (int)Math.Round(drawOffsetY + ((1.0 - sourceRect.MaxV) * image.Height), MidpointRounding.AwayFromZero),
                (int)Math.Round(sourceRect.Width * image.Width, MidpointRounding.AwayFromZero),
                (int)Math.Round(sourceRect.Height * image.Height, MidpointRounding.AwayFromZero),
                canvasWidth,
                canvasHeight)
            : TextureUvRect.FromTopLeftPixelRect(
                drawOffsetX,
                drawOffsetY,
                image.Width,
                image.Height,
                canvasWidth,
                canvasHeight);
        generatedTexture = new GeneratedTerrainTexture(
            CreateTextureSource(canvasImage, primaryUsage),
            occupiedUvRect,
            CreateUsagesWithPrimaryFirst(primaryUsage, usages));
        return true;
    }

    private static TerrainTextureSourceUsage[] CreateUsagesWithPrimaryFirst(
        TerrainTextureSourceUsage? primaryUsage,
        IReadOnlyList<TerrainTextureSourceUsage> usages)
    {
        if (primaryUsage is null)
        {
            return usages.ToArray();
        }

        return
        [
            primaryUsage,
            .. usages
                .Where(usage => usage != primaryUsage),
        ];
    }

    private static TextureUvRect CreateOccupiedUvRect(
        int x,
        int y,
        int width,
        int height,
        int canvasWidth,
        int canvasHeight)
    {
        int clampedX = Math.Clamp(x, 0, canvasWidth - 1);
        int clampedY = Math.Clamp(y, 0, canvasHeight - 1);
        return TextureUvRect.FromTopLeftPixelRect(
            clampedX,
            clampedY,
            Math.Min(width, canvasWidth - clampedX),
            Math.Min(height, canvasHeight - clampedY),
            canvasWidth,
            canvasHeight);
    }

    private static Image<Rgba32> CreateOpaqueGroundImage(Image<Rgba32> image)
    {
        Image<Rgba32> opaqueImage = new(image.Width, image.Height, DefaultDemGroundFillColor);
        opaqueImage.Mutate(context => context.DrawImage(image, new Point(0, 0), 1.0f));
        return opaqueImage;
    }

    private static Image<Rgba32> ResizeToMaxTextureSize(Image<Rgba32> image, int maxTextureSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTextureSize);

        if (image.Width <= maxTextureSize && image.Height <= maxTextureSize)
        {
            return image.Clone();
        }

        return image.Clone(context => context.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(maxTextureSize, maxTextureSize),
            Sampler = KnownResamplers.Lanczos3,
        }));
    }

    private static Image<Rgba32> ResizeSourceImage(Image<Rgba32> image, int width, int height)
    {
        return image.Width == width && image.Height == height
            ? image.Clone()
            : image.Clone(context => context.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Stretch,
                Size = new Size(width, height),
                Sampler = KnownResamplers.Lanczos3,
            }));
    }

    private static bool HasRenderablePixels(Image<Rgba32> image)
    {
        bool hasRenderablePixels = false;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height && !hasRenderablePixels; y++)
            {
                ReadOnlySpan<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (row[x].A > 0)
                    {
                        hasRenderablePixels = true;
                        break;
                    }
                }
            }
        });

        return hasRenderablePixels;
    }

    private static bool HasTransparentPixels(Image<Rgba32> image)
    {
        bool hasTransparentPixels = false;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height && !hasTransparentPixels; y++)
            {
                ReadOnlySpan<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (row[x].A == 0)
                    {
                        hasTransparentPixels = true;
                        break;
                    }
                }
            }
        });

        return hasTransparentPixels;
    }

    private static bool FillTransparentPixels(Image<Rgba32> destination, Image<Rgba32> fallback)
    {
        bool filledAny = false;
        for (int y = 0; y < destination.Height; y++)
        {
            for (int x = 0; x < destination.Width; x++)
            {
                if (destination[x, y].A > 0)
                {
                    continue;
                }

                Rgba32 fallbackPixel = fallback[x, y];
                if (fallbackPixel.A > 0)
                {
                    destination[x, y] = fallbackPixel;
                    filledAny = true;
                }
            }
        }

        return filledAny;
    }

    private static string DescribeTerrainTextureSources(IEnumerable<TerrainTextureSource> sources)
    {
        return string.Join(
            ", ",
            sources.Select(static source => source switch
            {
                TerrainTextureTileSource tileSource => $"tile:{tileSource.ZoomLevel}:{tileSource.UrlTemplate}",
                TerrainTextureGeoReferencedRasterSource rasterSource => $"georaster:{rasterSource.ContentSource.Description}",
                _ => source.GetType().Name,
            }));
    }

    private static ITextureImportSource CreateTextureSource(Image<Rgba32> image, TerrainTextureSourceUsage? usage)
    {
        return TextureImportSourceFactory.CreateGeneratedImageFromClone(
            image,
            usage is null ? "terrain:default-ground" : $"terrain:{usage.TextureImportName}",
            SrgbTextureColorProfile);
    }

    private sealed record CachedTerrainTexture(GeneratedTerrainTexture GeneratedTexture);

}
