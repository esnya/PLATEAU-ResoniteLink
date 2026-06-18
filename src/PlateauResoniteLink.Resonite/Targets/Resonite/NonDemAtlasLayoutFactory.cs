using System.Collections.Generic;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal interface INonDemAtlasLayoutFactory
{
    bool CanFit(IReadOnlyList<NonDemAtlasBatchEntry> entries);

    bool TryCreate(
        IReadOnlyList<NonDemAtlasBatchEntry> entries,
        out NonDemAtlasLayout<NonDemAtlasBatchEntry>? layout);
}

internal sealed class NonDemAtlasLayoutFactory(int atlasMaxSize, int tilePaddingPixels) : INonDemAtlasLayoutFactory
{
    public bool CanFit(IReadOnlyList<NonDemAtlasBatchEntry> entries)
    {
        return TryCreate(entries, out _);
    }

    public bool TryCreate(
        IReadOnlyList<NonDemAtlasBatchEntry> entries,
        out NonDemAtlasLayout<NonDemAtlasBatchEntry>? layout)
    {
        NonDemAtlasLayoutPacker packer = new(atlasMaxSize, tilePaddingPixels);
        return packer.TryCreate(
            entries,
            static entry => new NonDemAtlasTileSize(entry.Tile.Image.Width, entry.Tile.Image.Height),
            out layout);
    }
}
