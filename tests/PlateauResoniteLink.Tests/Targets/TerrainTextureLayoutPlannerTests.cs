using System;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class TerrainTextureLayoutPlannerTests
{
    [Fact]
    public void CreateReturnsTileAndCropLayoutForWorldScaleOverlay()
    {
        TerrainTextureLayoutPlan plan = TerrainTextureLayoutPlanner.Create(
            new GeographicRectangle(
                MinLatitude: 0.0,
                MaxLatitude: WebMercatorTileMath.MaxLatitude,
                MinLongitude: -180.0,
                MaxLongitude: 180.0),
            1);

        Assert.Equal(0, plan.MinTileX);
        Assert.Equal(1, plan.MaxTileX);
        Assert.Equal(-1, plan.MinTileY);
        Assert.Equal(0, plan.MaxTileY);
        Assert.Equal(512, plan.StitchedWidth);
        Assert.Equal(512, plan.StitchedHeight);
        Assert.Equal(0, plan.CropLeft);
        Assert.InRange(plan.CropTop, 255, 256);
        Assert.Equal(512, plan.CropWidth);
        Assert.InRange(plan.CropHeight, 256, 257);
    }

    [Fact]
    public void CreateRejectsDegenerateOverlayBounds()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => TerrainTextureLayoutPlanner.Create(
            new GeographicRectangle(
                MinLatitude: 35.0,
                MaxLatitude: 35.0,
                MinLongitude: 139.0,
                MaxLongitude: 139.0),
            1));

        Assert.Contains("degenerate geographic bounds", exception.Message, StringComparison.Ordinal);
    }
}
