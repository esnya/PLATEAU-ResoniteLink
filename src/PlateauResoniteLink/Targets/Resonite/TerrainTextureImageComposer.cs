using System;
using System.Collections.Generic;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class TerrainTextureImageComposer
{
    // Approximate dry brown soil tone (Munsell 10YR 5/3 family) for uncovered DEM texels.
    internal static readonly Rgba32 DefaultGroundFillColor = new(181, 176, 166, byte.MaxValue);

    internal static GeneratedTerrainTexture CreateGeneratedTexture(
        Image<Rgba32> image,
        int maxTextureSize,
        TerrainTextureSource usedSource,
        IReadOnlyList<TerrainTextureSource> usedSources,
        TextureUvRect? sourceOccupiedUvRect)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTextureSize);
        using Image<Rgba32> opaqueImage = CreateOpaqueGroundImage(image);

        if (TryCreatePowerOfTwoCanvasTexture(opaqueImage, maxTextureSize, usedSource, usedSources, sourceOccupiedUvRect, out GeneratedTerrainTexture? generatedTexture))
        {
            return generatedTexture!;
        }

        int fallbackMaxTextureSize = RoundDownToPowerOfTwo(maxTextureSize);
        using Image<Rgba32> resizedImage = ResizeToMaxTextureSize(opaqueImage, fallbackMaxTextureSize);
        if (TryCreatePowerOfTwoCanvasTexture(resizedImage, fallbackMaxTextureSize, usedSource, usedSources, null, out generatedTexture))
        {
            return generatedTexture!;
        }

        throw new InvalidOperationException(
            $"Terrain texture fallback failed to fit into a power-of-two canvas within maxTextureSize={maxTextureSize}.");
    }

    internal static Image<Rgba32> ResizeSourceImage(Image<Rgba32> image, int width, int height)
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

    internal static bool HasRenderablePixels(Image<Rgba32> image)
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

    internal static bool HasTransparentPixels(Image<Rgba32> image)
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

    internal static bool FillTransparentPixels(Image<Rgba32> destination, Image<Rgba32> fallback)
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

    internal static int RoundUpToPowerOfTwo(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);

        int rounded = 1;
        while (rounded < value)
        {
            rounded <<= 1;
        }

        return rounded;
    }

    private static bool TryCreatePowerOfTwoCanvasTexture(
        Image<Rgba32> image,
        int maxTextureSize,
        TerrainTextureSource usedSource,
        IReadOnlyList<TerrainTextureSource> usedSources,
        TextureUvRect? sourceOccupiedUvRect,
        out GeneratedTerrainTexture? generatedTexture)
    {
        int canvasWidth = RoundUpToPowerOfTwo(image.Width);
        int canvasHeight = RoundUpToPowerOfTwo(image.Height);
        if (canvasWidth > maxTextureSize || canvasHeight > maxTextureSize)
        {
            generatedTexture = null;
            return false;
        }

        using Image<Rgba32> canvasImage = new(canvasWidth, canvasHeight, DefaultGroundFillColor);
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
            CreateRawTextureImport(canvasImage),
            occupiedUvRect,
            usedSource,
            usedSources.Distinct().ToArray());
        return true;
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
        Image<Rgba32> opaqueImage = new(image.Width, image.Height, DefaultGroundFillColor);
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

    private static int RoundDownToPowerOfTwo(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);

        int rounded = 1;
        while ((rounded << 1) > 0 && (rounded << 1) <= value)
        {
            rounded <<= 1;
        }

        return rounded;
    }

    private static ResoniteRawTextureImport CreateRawTextureImport(Image<Rgba32> image)
    {
        byte[] rawBytes = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(rawBytes);
        return new ResoniteRawTextureImport(
            image.Width,
            image.Height,
            "sRGB",
            rawBytes);
    }
}
