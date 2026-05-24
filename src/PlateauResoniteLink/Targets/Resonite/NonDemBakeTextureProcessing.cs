using System;
using System.Collections.Generic;

using PlateauResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class NonDemBakeTextureProcessing
{
    private const byte BackgroundDetectionAlphaThreshold = 16;

    internal static void ApplyBaseColor(Image<Rgba32> image, ResoniteColor color)
    {
        Rgba32 tint = ToPixel(color);
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Rgba32 pixel = image[x, y];
                image[x, y] = new Rgba32(
                    MultiplyChannel(pixel.R, tint.R),
                    MultiplyChannel(pixel.G, tint.G),
                    MultiplyChannel(pixel.B, tint.B),
                    MultiplyChannel(pixel.A, tint.A));
            }
        }
    }

    internal static Rgba32 MultiplyPixel(Rgba32 left, Rgba32 right)
    {
        return new Rgba32(
            MultiplyChannel(left.R, right.R),
            MultiplyChannel(left.G, right.G),
            MultiplyChannel(left.B, right.B),
            MultiplyChannel(left.A, right.A));
    }

    internal static Rgba32 ToPixel(ResoniteColor color)
    {
        return new Rgba32(
            (byte)Math.Round(Math.Clamp(color.R, 0.0, 1.0) * 255.0),
            (byte)Math.Round(Math.Clamp(color.G, 0.0, 1.0) * 255.0),
            (byte)Math.Round(Math.Clamp(color.B, 0.0, 1.0) * 255.0),
            (byte)Math.Round(Math.Clamp(color.A, 0.0, 1.0) * 255.0));
    }

    internal static ResoniteColor ToColor(Rgba32 color)
    {
        return new ResoniteColor(
            color.R / 255.0,
            color.G / 255.0,
            color.B / 255.0,
            color.A / 255.0);
    }

    internal static Image<Rgba32> BakeUsedUvRegion(
        Image<Rgba32> sourceImage,
        TextureUvRect uvBounds,
        int targetWidth,
        int targetHeight)
    {
        Image<Rgba32> bakedImage = new(targetWidth, targetHeight);
        for (int y = 0; y < targetHeight; y++)
        {
            double normalizedV = 1.0 - ((y + 0.5) / targetHeight);
            for (int x = 0; x < targetWidth; x++)
            {
                double normalizedU = (x + 0.5) / targetWidth;
                ScalarPair sourceUv = uvBounds.DenormalizeValue(normalizedU, normalizedV);
                bakedImage[x, y] = SampleWrappedPixelBilinear(sourceImage, sourceUv.X, sourceUv.Y);
            }
        }

        return bakedImage;
    }

    internal static Rgba32 DetectRepresentativeBackgroundColor(Image<Rgba32> image)
    {
        if (TryAverageBoundaryOpaquePixels(image, out Rgba32 boundaryAverage))
        {
            return boundaryAverage;
        }

        if (TryAverageOpaquePixels(image, out Rgba32 opaqueAverage))
        {
            return opaqueAverage;
        }

        if (TryAverageAllPixels(image, out Rgba32 allPixelAverage))
        {
            return new Rgba32(allPixelAverage.R, allPixelAverage.G, allPixelAverage.B, byte.MaxValue);
        }

        return new Rgba32(255, 255, 255, 255);
    }

    internal static void FillTransparentRgb(Image<Rgba32> image, Rgba32 backgroundColor)
    {
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Rgba32 pixel = image[x, y];
                if (pixel.A == byte.MaxValue)
                {
                    continue;
                }

                double alpha = pixel.A / 255.0;
                image[x, y] = new Rgba32(
                    BlendBackgroundChannel(pixel.R, backgroundColor.R, alpha),
                    BlendBackgroundChannel(pixel.G, backgroundColor.G, alpha),
                    BlendBackgroundChannel(pixel.B, backgroundColor.B, alpha),
                    pixel.A);
            }
        }
    }

    internal static bool TryGetUniformPixelColor(Image<Rgba32> image, out Rgba32 color)
    {
        color = default;
        if (image.Width <= 0 || image.Height <= 0)
        {
            return false;
        }

        Rgba32 firstPixel = image[0, 0];
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                if (!image[x, y].Equals(firstPixel))
                {
                    return false;
                }
            }
        }

        color = firstPixel;
        return true;
    }

    internal static Rgba32 ComputeWeightedBackgroundColor(
        IEnumerable<(Rgba32 Color, long Weight)> weightedColors)
    {
        long sumR = 0;
        long sumG = 0;
        long sumB = 0;
        long totalWeight = 0;
        foreach ((Rgba32 color, long sourceWeight) in weightedColors)
        {
            long weight = Math.Max(1, sourceWeight);
            sumR += color.R * weight;
            sumG += color.G * weight;
            sumB += color.B * weight;
            totalWeight += weight;
        }

        if (totalWeight == 0)
        {
            return new Rgba32(255, 255, 255, 255);
        }

        return new Rgba32(
            (byte)Math.Clamp(Math.Round(sumR / (double)totalWeight), 0.0, 255.0),
            (byte)Math.Clamp(Math.Round(sumG / (double)totalWeight), 0.0, 255.0),
            (byte)Math.Clamp(Math.Round(sumB / (double)totalWeight), 0.0, 255.0),
            byte.MaxValue);
    }

    internal static void FillUncoveredAtlasPixels(Image<Rgba32> atlasImage, bool[] atlasCoverage, Rgba32 backgroundColor)
    {
        for (int y = 0; y < atlasImage.Height; y++)
        {
            for (int x = 0; x < atlasImage.Width; x++)
            {
                int offset = (y * atlasImage.Width) + x;
                if (atlasCoverage[offset])
                {
                    continue;
                }

                atlasImage[x, y] = backgroundColor;
            }
        }
    }

    internal static void SetAtlasPixel(Image<Rgba32> atlasImage, bool[] atlasCoverage, int atlasWidth, int x, int y, Rgba32 pixel)
    {
        atlasImage[x, y] = pixel;
        atlasCoverage[(y * atlasWidth) + x] = true;
    }

    internal static void DrawAtlasTile<TEntry>(
        Image<Rgba32> atlasImage,
        bool[] atlasCoverage,
        int atlasWidth,
        int tilePaddingPixels,
        NonDemAtlasPlacement<TEntry> placement,
        Func<TEntry, Image<Rgba32>> getTileImage)
    {
        ArgumentNullException.ThrowIfNull(getTileImage);

        Image<Rgba32> tileImage = getTileImage(placement.Entry);
        for (int y = 0; y < tileImage.Height; y++)
        {
            for (int x = 0; x < tileImage.Width; x++)
            {
                SetAtlasPixel(
                    atlasImage,
                    atlasCoverage,
                    atlasWidth,
                    placement.InnerRect.X + x,
                    placement.InnerRect.Y + y,
                    tileImage[x, y]);
            }
        }

        for (int y = 0; y < tileImage.Height; y++)
        {
            Rgba32 leftEdge = atlasImage[placement.InnerRect.X, placement.InnerRect.Y + y];
            Rgba32 rightEdge = atlasImage[placement.InnerRect.X + placement.InnerRect.Width - 1, placement.InnerRect.Y + y];
            for (int pad = 1; pad <= tilePaddingPixels; pad++)
            {
                SetAtlasPixel(
                    atlasImage,
                    atlasCoverage,
                    atlasWidth,
                    placement.InnerRect.X - pad,
                    placement.InnerRect.Y + y,
                    leftEdge);
                SetAtlasPixel(
                    atlasImage,
                    atlasCoverage,
                    atlasWidth,
                    placement.InnerRect.X + placement.InnerRect.Width - 1 + pad,
                    placement.InnerRect.Y + y,
                    rightEdge);
            }
        }

        int fullWidth = placement.InnerRect.Width + (tilePaddingPixels * 2);
        for (int pad = 1; pad <= tilePaddingPixels; pad++)
        {
            int sourceTopY = placement.InnerRect.Y;
            int sourceBottomY = placement.InnerRect.Y + placement.InnerRect.Height - 1;
            int targetTopY = placement.InnerRect.Y - pad;
            int targetBottomY = placement.InnerRect.Y + placement.InnerRect.Height - 1 + pad;
            for (int x = 0; x < fullWidth; x++)
            {
                int sampleX = placement.InnerRect.X - tilePaddingPixels + x;
                SetAtlasPixel(
                    atlasImage,
                    atlasCoverage,
                    atlasWidth,
                    sampleX,
                    targetTopY,
                    atlasImage[sampleX, sourceTopY]);
                SetAtlasPixel(
                    atlasImage,
                    atlasCoverage,
                    atlasWidth,
                    sampleX,
                    targetBottomY,
                    atlasImage[sampleX, sourceBottomY]);
            }
        }
    }

    private static bool TryAverageBoundaryOpaquePixels(Image<Rgba32> image, out Rgba32 color)
    {
        long sumR = 0;
        long sumG = 0;
        long sumB = 0;
        long count = 0;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Rgba32 pixel = image[x, y];
                if (pixel.A <= BackgroundDetectionAlphaThreshold || !TouchesTransparentNeighbor(image, x, y))
                {
                    continue;
                }

                sumR += pixel.R;
                sumG += pixel.G;
                sumB += pixel.B;
                count++;
            }
        }

        color = count == 0
            ? default
            : new Rgba32(
                (byte)Math.Clamp(Math.Round(sumR / (double)count), 0.0, 255.0),
                (byte)Math.Clamp(Math.Round(sumG / (double)count), 0.0, 255.0),
                (byte)Math.Clamp(Math.Round(sumB / (double)count), 0.0, 255.0),
                byte.MaxValue);
        return count > 0;
    }

    private static bool TouchesTransparentNeighbor(Image<Rgba32> image, int x, int y)
    {
        return IsTransparentOrOutOfBounds(image, x - 1, y)
            || IsTransparentOrOutOfBounds(image, x + 1, y)
            || IsTransparentOrOutOfBounds(image, x, y - 1)
            || IsTransparentOrOutOfBounds(image, x, y + 1);
    }

    private static bool IsTransparentOrOutOfBounds(Image<Rgba32> image, int x, int y)
    {
        if (x < 0 || y < 0 || x >= image.Width || y >= image.Height)
        {
            return true;
        }

        return image[x, y].A <= BackgroundDetectionAlphaThreshold;
    }

    private static bool TryAverageOpaquePixels(Image<Rgba32> image, out Rgba32 color)
    {
        return TryAveragePixels(image, static pixel => pixel.A > BackgroundDetectionAlphaThreshold, out color);
    }

    private static bool TryAverageAllPixels(Image<Rgba32> image, out Rgba32 color)
    {
        return TryAveragePixels(image, static _ => true, out color);
    }

    private static bool TryAveragePixels(Image<Rgba32> image, Func<Rgba32, bool> predicate, out Rgba32 color)
    {
        long sumR = 0;
        long sumG = 0;
        long sumB = 0;
        long count = 0;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Rgba32 pixel = image[x, y];
                if (!predicate(pixel))
                {
                    continue;
                }

                sumR += pixel.R;
                sumG += pixel.G;
                sumB += pixel.B;
                count++;
            }
        }

        color = count == 0
            ? default
            : new Rgba32(
                (byte)Math.Clamp(Math.Round(sumR / (double)count), 0.0, 255.0),
                (byte)Math.Clamp(Math.Round(sumG / (double)count), 0.0, 255.0),
                (byte)Math.Clamp(Math.Round(sumB / (double)count), 0.0, 255.0),
                byte.MaxValue);
        return count > 0;
    }

    private static byte MultiplyChannel(byte left, byte right)
    {
        return (byte)Math.Clamp((left * right + 127) / 255, 0, 255);
    }

    private static byte BlendBackgroundChannel(byte foreground, byte background, double alpha)
    {
        double blended = (foreground * alpha) + (background * (1.0 - alpha));
        return (byte)Math.Clamp(Math.Round(blended), 0.0, 255.0);
    }

    private static Rgba32 SampleWrappedPixelBilinear(Image<Rgba32> sourceImage, double u, double v)
    {
        double wrappedU = WrapUvCoordinate(u);
        double wrappedV = WrapUvCoordinate(v);
        double sourceX = (wrappedU * sourceImage.Width) - 0.5;
        double sourceY = ((1.0 - wrappedV) * sourceImage.Height) - 0.5;
        int x0 = (int)Math.Floor(sourceX);
        int y0 = (int)Math.Floor(sourceY);
        int x1 = x0 + 1;
        int y1 = y0 + 1;
        double tx = sourceX - x0;
        double ty = sourceY - y0;

        Rgba32 topLeft = sourceImage[WrapPixelCoordinate(x0, sourceImage.Width), WrapPixelCoordinate(y0, sourceImage.Height)];
        Rgba32 topRight = sourceImage[WrapPixelCoordinate(x1, sourceImage.Width), WrapPixelCoordinate(y0, sourceImage.Height)];
        Rgba32 bottomLeft = sourceImage[WrapPixelCoordinate(x0, sourceImage.Width), WrapPixelCoordinate(y1, sourceImage.Height)];
        Rgba32 bottomRight = sourceImage[WrapPixelCoordinate(x1, sourceImage.Width), WrapPixelCoordinate(y1, sourceImage.Height)];
        return LerpPixels(topLeft, topRight, bottomLeft, bottomRight, tx, ty);
    }

    private static double WrapUvCoordinate(double value)
    {
        double wrapped = value - Math.Floor(value);
        return wrapped >= 1.0 ? 0.0 : wrapped;
    }

    private static int WrapPixelCoordinate(int value, int length)
    {
        int wrapped = value % length;
        return wrapped < 0 ? wrapped + length : wrapped;
    }

    private static Rgba32 LerpPixels(
        Rgba32 topLeft,
        Rgba32 topRight,
        Rgba32 bottomLeft,
        Rgba32 bottomRight,
        double tx,
        double ty)
    {
        return new Rgba32(
            LerpChannel(topLeft.R, topRight.R, bottomLeft.R, bottomRight.R, tx, ty),
            LerpChannel(topLeft.G, topRight.G, bottomLeft.G, bottomRight.G, tx, ty),
            LerpChannel(topLeft.B, topRight.B, bottomLeft.B, bottomRight.B, tx, ty),
            LerpChannel(topLeft.A, topRight.A, bottomLeft.A, bottomRight.A, tx, ty));
    }

    private static byte LerpChannel(
        byte topLeft,
        byte topRight,
        byte bottomLeft,
        byte bottomRight,
        double tx,
        double ty)
    {
        double top = topLeft + ((topRight - topLeft) * tx);
        double bottom = bottomLeft + ((bottomRight - bottomLeft) * tx);
        double value = top + ((bottom - top) * ty);
        return (byte)Math.Clamp(Math.Round(value), 0.0, 255.0);
    }
}
