using System;

using PlateauResoniteLink.Targets.Resonite;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class ResoniteConstructionGeometryTests
{
    [Theory]
    [InlineData(1, 2, "Width")]
    [InlineData(2, 1, "Height")]
    public void TerrainGridConstructorRejectsDegenerateGridDimensions(int width, int height, string expectedParamName)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ResoniteTerrainGridGeometry(
                Width: width,
                Height: height,
                Size: new ResoniteFloat2(1.0, 1.0),
                MinHeight: 0.0,
                MaxHeight: 1.0,
                HeightSamples: [0.0, 1.0]));

        Assert.Equal(expectedParamName, exception.ParamName);
    }

    [Fact]
    public void TerrainGridConstructorRejectsHeightSampleCountMismatch()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new ResoniteTerrainGridGeometry(
                Width: 2,
                Height: 2,
                Size: new ResoniteFloat2(1.0, 1.0),
                MinHeight: 0.0,
                MaxHeight: 1.0,
                HeightSamples: [0.0, 1.0, 2.0]));

        Assert.Equal("HeightSamples", exception.ParamName);
    }

    [Fact]
    public void TerrainGridPropertiesExposeSampleCountAndHeightRange()
    {
        ResoniteTerrainGridGeometry geometry = new(
            Width: 2,
            Height: 2,
            Size: new ResoniteFloat2(1.0, 1.0),
            MinHeight: -2.0,
            MaxHeight: 3.5,
            HeightSamples: [-2.0, 0.0, 1.0, 3.5]);

        Assert.Equal(4, geometry.SampleCount);
        Assert.Equal(5.5, geometry.HeightRange);
    }
}
