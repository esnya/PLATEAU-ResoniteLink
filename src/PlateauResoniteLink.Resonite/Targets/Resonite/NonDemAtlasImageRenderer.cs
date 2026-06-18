using System;
using System.Collections.Generic;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal interface INonDemAtlasImageRenderer
{
    void Draw(
        Image<Rgba32> atlasImage,
        IReadOnlyList<NonDemAtlasPlacement<NonDemAtlasBatchEntry>> placements);
}

internal sealed class NonDemAtlasImageRenderer(int tilePaddingPixels) : INonDemAtlasImageRenderer
{
    public void Draw(
        Image<Rgba32> atlasImage,
        IReadOnlyList<NonDemAtlasPlacement<NonDemAtlasBatchEntry>> placements)
    {
        bool[] atlasCoverage = new bool[atlasImage.Width * atlasImage.Height];
        foreach (NonDemAtlasPlacement<NonDemAtlasBatchEntry> placement in placements)
        {
            DrawTile(atlasImage, atlasCoverage, placement);
        }

        FillUncoveredPixels(atlasImage, atlasCoverage, ComputeBackgroundColor(placements));
    }

    private void DrawTile(
        Image<Rgba32> atlasImage,
        bool[] atlasCoverage,
        NonDemAtlasPlacement<NonDemAtlasBatchEntry> placement)
    {
        for (int y = 0; y < placement.Entry.Tile.Image.Height; y++)
        {
            for (int x = 0; x < placement.Entry.Tile.Image.Width; x++)
            {
                SetPixel(
                    atlasImage,
                    atlasCoverage,
                    placement.InnerRect.X + x,
                    placement.InnerRect.Y + y,
                    placement.Entry.Tile.Image[x, y]);
            }
        }

        for (int y = 0; y < placement.Entry.Tile.Image.Height; y++)
        {
            Rgba32 leftEdge = atlasImage[placement.InnerRect.X, placement.InnerRect.Y + y];
            Rgba32 rightEdge = atlasImage[placement.InnerRect.X + placement.InnerRect.Width - 1, placement.InnerRect.Y + y];
            for (int pad = 1; pad <= tilePaddingPixels; pad++)
            {
                SetPixel(
                    atlasImage,
                    atlasCoverage,
                    placement.InnerRect.X - pad,
                    placement.InnerRect.Y + y,
                    leftEdge);
                SetPixel(
                    atlasImage,
                    atlasCoverage,
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
                SetPixel(
                    atlasImage,
                    atlasCoverage,
                    sampleX,
                    targetTopY,
                    atlasImage[sampleX, sourceTopY]);
                SetPixel(
                    atlasImage,
                    atlasCoverage,
                    sampleX,
                    targetBottomY,
                    atlasImage[sampleX, sourceBottomY]);
            }
        }
    }

    private static Rgba32 ComputeBackgroundColor(
        IReadOnlyList<NonDemAtlasPlacement<NonDemAtlasBatchEntry>> placements)
    {
        long sumR = 0;
        long sumG = 0;
        long sumB = 0;
        long totalWeight = 0;
        foreach (NonDemAtlasPlacement<NonDemAtlasBatchEntry> placement in placements)
        {
            long weight = Math.Max(1, placement.Entry.Tile.Image.Width * placement.Entry.Tile.Image.Height);
            sumR += placement.Entry.Tile.BackgroundColor.R * weight;
            sumG += placement.Entry.Tile.BackgroundColor.G * weight;
            sumB += placement.Entry.Tile.BackgroundColor.B * weight;
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

    private static void FillUncoveredPixels(Image<Rgba32> atlasImage, bool[] atlasCoverage, Rgba32 backgroundColor)
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

    private static void SetPixel(Image<Rgba32> atlasImage, bool[] atlasCoverage, int x, int y, Rgba32 pixel)
    {
        atlasImage[x, y] = pixel;
        atlasCoverage[(y * atlasImage.Width) + x] = true;
    }
}
