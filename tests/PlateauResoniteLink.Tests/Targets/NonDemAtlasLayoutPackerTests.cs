
using PlateauResoniteLink.Targets.Resonite;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class NonDemAtlasLayoutPackerTests
{
    [Fact]
    public void TryCreateUsesSmallestPowerOfTwoCanvasThatFitsPaddedTiles()
    {
        NonDemAtlasLayoutPacker packer = new(atlasMaxSize: 16, tilePaddingPixels: 1);

        bool created = packer.TryCreate(
            [
                new Tile("a", 2, 2),
                new Tile("b", 2, 2),
            ],
            static tile => new NonDemAtlasTileSize(tile.Width, tile.Height),
            out NonDemAtlasLayout<Tile>? layout);

        Assert.True(created);
        Assert.NotNull(layout);
        Assert.Equal(8, layout.Width);
        Assert.Equal(4, layout.Height);
        Assert.Collection(
            layout.Placements,
            placement =>
            {
                Assert.Equal("a", placement.Entry.Id);
                Assert.Equal(new NonDemAtlasRect(0, 0, 4, 4), placement.OuterRect);
                Assert.Equal(new NonDemAtlasRect(1, 1, 2, 2), placement.InnerRect);
            },
            placement =>
            {
                Assert.Equal("b", placement.Entry.Id);
                Assert.Equal(new NonDemAtlasRect(4, 0, 4, 4), placement.OuterRect);
                Assert.Equal(new NonDemAtlasRect(5, 1, 2, 2), placement.InnerRect);
            });
    }

    [Fact]
    public void TryCreateRejectsTileWhosePaddedSizeExceedsAtlas()
    {
        NonDemAtlasLayoutPacker packer = new(atlasMaxSize: 4, tilePaddingPixels: 1);

        bool created = packer.TryCreate(
            [new Tile("oversized", 3, 3)],
            static tile => new NonDemAtlasTileSize(tile.Width, tile.Height),
            out NonDemAtlasLayout<Tile>? layout);

        Assert.False(created);
        Assert.Null(layout);
    }

    private sealed record Tile(string Id, int Width, int Height);
}
