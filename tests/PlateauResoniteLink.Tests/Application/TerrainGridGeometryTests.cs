using System;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Application;

public sealed class TerrainGridGeometryTests
{
    [Theory]
    [InlineData(1, 2, "Width")]
    [InlineData(2, 1, "Height")]
    public void ConstructorRejectsDegenerateGridDimensions(int width, int height, string expectedParamName)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new TerrainGridGeometry(
                Width: width,
                Height: height,
                Size: new Float2(1.0, 1.0),
                MinHeight: 0.0,
                MaxHeight: 1.0,
                HeightSamples: [0.0, 1.0],
                SampleCoverage:
                [
                    TerrainGridSampleCoverage.Measured,
                    TerrainGridSampleCoverage.Measured,
                ]));

        Assert.Equal(expectedParamName, exception.ParamName);
    }

    [Fact]
    public void ConstructorRejectsHeightSampleCountMismatch()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new TerrainGridGeometry(
                Width: 2,
                Height: 2,
                Size: new Float2(1.0, 1.0),
                MinHeight: 0.0,
                MaxHeight: 1.0,
                HeightSamples: [0.0, 1.0, 2.0],
                SampleCoverage:
                [
                    TerrainGridSampleCoverage.Measured,
                    TerrainGridSampleCoverage.Measured,
                    TerrainGridSampleCoverage.Measured,
                    TerrainGridSampleCoverage.Measured,
                ]));

        Assert.Equal("HeightSamples", exception.ParamName);
    }

    [Fact]
    public void ConstructorRejectsSampleCoverageCountMismatch()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new TerrainGridGeometry(
                Width: 2,
                Height: 2,
                Size: new Float2(1.0, 1.0),
                MinHeight: 0.0,
                MaxHeight: 1.0,
                HeightSamples: [0.0, 1.0, 2.0, 3.0],
                SampleCoverage:
                [
                    TerrainGridSampleCoverage.Measured,
                    TerrainGridSampleCoverage.Measured,
                    TerrainGridSampleCoverage.Measured,
                ]));

        Assert.Equal("SampleCoverage", exception.ParamName);
    }

    [Fact]
    public void PropertiesExposeTerrainGridHeightFrame()
    {
        TerrainGridGeometry geometry = new(
            Width: 2,
            Height: 2,
            Size: new Float2(1.0, 1.0),
            MinHeight: 5.0,
            MaxHeight: 12.0,
            HeightSamples: [5.0, 7.0, 9.0, 12.0],
            SampleCoverage:
            [
                TerrainGridSampleCoverage.Measured,
                TerrainGridSampleCoverage.Measured,
                TerrainGridSampleCoverage.Measured,
                TerrainGridSampleCoverage.Measured,
            ]);

        Assert.Equal(4, geometry.SampleCount);
        Assert.Equal(7.0, geometry.HeightRange);
        Assert.Equal(90.0, geometry.GetWorldBaseHeight(new Transform3D(new Float3(0.0, 102.0, 0.0))));
    }
}
