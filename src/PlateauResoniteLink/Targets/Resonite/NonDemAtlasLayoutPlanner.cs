using System;
using System.Collections.Generic;
using System.Linq;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class NonDemAtlasLayoutPlanner
{
    internal static bool TryCreateLayout<TEntry>(
        IReadOnlyList<TEntry> entries,
        int atlasMaxSize,
        int tilePaddingPixels,
        Func<TEntry, NonDemAtlasTileSize> getTileSize,
        out NonDemAtlasLayout<TEntry>? layout)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(getTileSize);

        AtlasSizeRequirements requirements = ComputeAtlasSizeRequirements(
            entries,
            atlasMaxSize,
            tilePaddingPixels,
            getTileSize);
        if (!requirements.IsValid)
        {
            layout = null;
            return false;
        }

        foreach (AtlasCanvasCandidate candidate in EnumerateAtlasCanvasCandidates(requirements, atlasMaxSize))
        {
            if (TryCreateLayoutForCanvas(
                entries,
                candidate.Width,
                candidate.Height,
                tilePaddingPixels,
                getTileSize,
                out layout))
            {
                return true;
            }
        }

        layout = null;
        return false;
    }

    private static bool TryCreateLayoutForCanvas<TEntry>(
        IReadOnlyList<TEntry> entries,
        int atlasWidth,
        int atlasHeight,
        int tilePaddingPixels,
        Func<TEntry, NonDemAtlasTileSize> getTileSize,
        out NonDemAtlasLayout<TEntry>? layout)
    {
        List<NonDemAtlasPlacement<TEntry>> placements = [];
        List<NonDemAtlasRect> freeRectangles = [new NonDemAtlasRect(0, 0, atlasWidth, atlasHeight)];

        foreach (TEntry entry in entries)
        {
            NonDemAtlasEntrySize entrySize = GetAtlasEntrySize(entry, tilePaddingPixels, getTileSize);
            if (entrySize.PaddedWidth > atlasWidth || entrySize.PaddedHeight > atlasHeight)
            {
                layout = null;
                return false;
            }

            if (!TryChooseFreeRectangle(
                    freeRectangles,
                    entrySize.PaddedWidth,
                    entrySize.PaddedHeight,
                    out NonDemAtlasRect selectedRect))
            {
                layout = null;
                return false;
            }

            NonDemAtlasTileSize tileSize = getTileSize(entry);
            NonDemAtlasRect outerRect = new(selectedRect.X, selectedRect.Y, entrySize.PaddedWidth, entrySize.PaddedHeight);
            NonDemAtlasRect innerRect = new(
                selectedRect.X + tilePaddingPixels,
                selectedRect.Y + tilePaddingPixels,
                tileSize.Width,
                tileSize.Height);
            placements.Add(new NonDemAtlasPlacement<TEntry>(entry, outerRect, innerRect));
            SplitFreeRectangles(freeRectangles, outerRect);
            PruneFreeRectangles(freeRectangles);
        }

        layout = new NonDemAtlasLayout<TEntry>(
            atlasWidth,
            atlasHeight,
            placements);
        return true;
    }

    private static AtlasSizeRequirements ComputeAtlasSizeRequirements<TEntry>(
        IReadOnlyList<TEntry> entries,
        int atlasMaxSize,
        int tilePaddingPixels,
        Func<TEntry, NonDemAtlasTileSize> getTileSize)
    {
        long requiredArea = 0;
        int minWidth = 1;
        int minHeight = 1;

        foreach (TEntry entry in entries)
        {
            NonDemAtlasEntrySize entrySize = GetAtlasEntrySize(entry, tilePaddingPixels, getTileSize);
            if (entrySize.PaddedWidth > atlasMaxSize || entrySize.PaddedHeight > atlasMaxSize)
            {
                return AtlasSizeRequirements.Invalid;
            }

            minWidth = Math.Max(minWidth, entrySize.PaddedWidth);
            minHeight = Math.Max(minHeight, entrySize.PaddedHeight);
            requiredArea += (long)entrySize.PaddedWidth * entrySize.PaddedHeight;
        }

        return new AtlasSizeRequirements(minWidth, minHeight, requiredArea);
    }

    private static IEnumerable<AtlasCanvasCandidate> EnumerateAtlasCanvasCandidates(
        AtlasSizeRequirements requirements,
        int atlasMaxSize)
    {
        List<int> widthCandidates = EnumeratePowerOfTwoEdges(requirements.MinWidth, atlasMaxSize).ToList();
        List<int> heightCandidates = EnumeratePowerOfTwoEdges(requirements.MinHeight, atlasMaxSize).ToList();

        return widthCandidates
            .SelectMany(
                width => heightCandidates,
                (width, height) => new AtlasCanvasCandidate(width, height, requirements))
            .OrderBy(static candidate => candidate.Area)
            .ThenBy(static candidate => candidate.AreaSlack)
            .ThenBy(static candidate => candidate.DimensionSlack)
            .ThenBy(static candidate => candidate.Height)
            .ThenBy(static candidate => candidate.Width);
    }

    private static IEnumerable<int> EnumeratePowerOfTwoEdges(int minimumEdge, int maxEdge)
    {
        if (minimumEdge > maxEdge)
        {
            yield break;
        }

        for (int edge = 1; edge > 0 && edge <= maxEdge; edge <<= 1)
        {
            if (edge >= minimumEdge)
            {
                yield return edge;
            }
        }
    }

    private static NonDemAtlasEntrySize GetAtlasEntrySize<TEntry>(
        TEntry entry,
        int tilePaddingPixels,
        Func<TEntry, NonDemAtlasTileSize> getTileSize)
    {
        NonDemAtlasTileSize tileSize = getTileSize(entry);
        return new NonDemAtlasEntrySize(
            tileSize.Width + (tilePaddingPixels * 2),
            tileSize.Height + (tilePaddingPixels * 2));
    }

    private static bool TryChooseFreeRectangle(
        IReadOnlyList<NonDemAtlasRect> freeRectangles,
        int requiredWidth,
        int requiredHeight,
        out NonDemAtlasRect selectedRect)
    {
        selectedRect = default;
        bool found = false;
        int bestAreaFit = int.MaxValue;
        int bestShortSideFit = int.MaxValue;

        foreach (NonDemAtlasRect freeRect in freeRectangles)
        {
            if (requiredWidth > freeRect.Width || requiredHeight > freeRect.Height)
            {
                continue;
            }

            int areaFit = (freeRect.Width * freeRect.Height) - (requiredWidth * requiredHeight);
            int shortSideFit = Math.Min(freeRect.Width - requiredWidth, freeRect.Height - requiredHeight);
            if (areaFit < bestAreaFit
                || (areaFit == bestAreaFit && shortSideFit < bestShortSideFit)
                || (areaFit == bestAreaFit && shortSideFit == bestShortSideFit
                    && (freeRect.Y < selectedRect.Y
                        || (freeRect.Y == selectedRect.Y && freeRect.X < selectedRect.X))))
            {
                selectedRect = freeRect;
                bestAreaFit = areaFit;
                bestShortSideFit = shortSideFit;
                found = true;
            }
        }

        return found;
    }

    private static void SplitFreeRectangles(List<NonDemAtlasRect> freeRectangles, NonDemAtlasRect usedRect)
    {
        for (int index = freeRectangles.Count - 1; index >= 0; index--)
        {
            NonDemAtlasRect freeRect = freeRectangles[index];
            if (!Intersects(freeRect, usedRect))
            {
                continue;
            }

            freeRectangles.RemoveAt(index);

            if (usedRect.X > freeRect.X)
            {
                freeRectangles.Add(new NonDemAtlasRect(
                    freeRect.X,
                    freeRect.Y,
                    usedRect.X - freeRect.X,
                    freeRect.Height));
            }

            if (usedRect.X + usedRect.Width < freeRect.X + freeRect.Width)
            {
                freeRectangles.Add(new NonDemAtlasRect(
                    usedRect.X + usedRect.Width,
                    freeRect.Y,
                    (freeRect.X + freeRect.Width) - (usedRect.X + usedRect.Width),
                    freeRect.Height));
            }

            if (usedRect.Y > freeRect.Y)
            {
                freeRectangles.Add(new NonDemAtlasRect(
                    freeRect.X,
                    freeRect.Y,
                    freeRect.Width,
                    usedRect.Y - freeRect.Y));
            }

            if (usedRect.Y + usedRect.Height < freeRect.Y + freeRect.Height)
            {
                freeRectangles.Add(new NonDemAtlasRect(
                    freeRect.X,
                    usedRect.Y + usedRect.Height,
                    freeRect.Width,
                    (freeRect.Y + freeRect.Height) - (usedRect.Y + usedRect.Height)));
            }
        }
    }

    private static void PruneFreeRectangles(List<NonDemAtlasRect> freeRectangles)
    {
        for (int leftIndex = freeRectangles.Count - 1; leftIndex >= 0; leftIndex--)
        {
            NonDemAtlasRect left = freeRectangles[leftIndex];
            for (int rightIndex = freeRectangles.Count - 1; rightIndex >= 0; rightIndex--)
            {
                if (leftIndex == rightIndex)
                {
                    continue;
                }

                NonDemAtlasRect right = freeRectangles[rightIndex];
                if (Contains(right, left))
                {
                    freeRectangles.RemoveAt(leftIndex);
                    break;
                }
            }
        }
    }

    private static bool Intersects(NonDemAtlasRect left, NonDemAtlasRect right)
    {
        return left.X < right.X + right.Width
            && left.X + left.Width > right.X
            && left.Y < right.Y + right.Height
            && left.Y + left.Height > right.Y;
    }

    private static bool Contains(NonDemAtlasRect outer, NonDemAtlasRect inner)
    {
        return inner.X >= outer.X
            && inner.Y >= outer.Y
            && inner.X + inner.Width <= outer.X + outer.Width
            && inner.Y + inner.Height <= outer.Y + outer.Height;
    }

    private readonly record struct AtlasSizeRequirements(
        int MinWidth,
        int MinHeight,
        long RequiredArea)
    {
        internal static AtlasSizeRequirements Invalid => new(0, 0, 0);

        internal bool IsValid => MinWidth > 0 && MinHeight > 0;
    }

    private readonly record struct AtlasCanvasCandidate(
        int Width,
        int Height,
        AtlasSizeRequirements Requirements)
    {
        internal long Area => (long)Width * Height;

        internal long AreaSlack => Area - Requirements.RequiredArea;

        internal int DimensionSlack => (Width - Requirements.MinWidth) + (Height - Requirements.MinHeight);
    }

    private readonly record struct NonDemAtlasEntrySize(
        int PaddedWidth,
        int PaddedHeight);
}

internal sealed record NonDemAtlasLayout<TEntry>(
    int Width,
    int Height,
    IReadOnlyList<NonDemAtlasPlacement<TEntry>> Placements);

internal sealed record NonDemAtlasPlacement<TEntry>(
    TEntry Entry,
    NonDemAtlasRect OuterRect,
    NonDemAtlasRect InnerRect);

internal readonly record struct NonDemAtlasTileSize(
    int Width,
    int Height);

internal readonly record struct NonDemAtlasRect(
    int X,
    int Y,
    int Width,
    int Height);
