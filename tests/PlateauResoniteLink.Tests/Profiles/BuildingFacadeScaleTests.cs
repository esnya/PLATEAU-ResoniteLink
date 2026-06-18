using PlateauResoniteLink.Plateau.Application.Importing.Plateau;


namespace PlateauResoniteLink.Tests.Profiles;

public sealed class BuildingFacadeScaleTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(3, 11.999)]
    [InlineData(0, 8.0)]
    [InlineData(-1, 8.0)]
    public void ClassifyTreatsSmallOrUnknownPositiveScaleAsLowRise(int? floorCount, double? measuredHeightMeters)
    {
        BuildingFacadeScale scale = BuildingFacadeScale.Classify(
            floorCount,
            measuredHeightMeters,
            geometryHeightMeters: null,
            footprintAreaSquareMeters: null);

        Assert.True(scale.LowRise);
        Assert.False(scale.MidOrHighRise);
        Assert.False(scale.Midrise);
        Assert.False(scale.Highrise);
        Assert.False(scale.Landmark);
    }

    [Theory]
    [InlineData(4, null)]
    [InlineData(null, 12.0)]
    public void ClassifyTreatsFourFloorsOrTwelveMetersAsMidOrHighRise(int? floorCount, double? measuredHeightMeters)
    {
        BuildingFacadeScale scale = BuildingFacadeScale.Classify(
            floorCount,
            measuredHeightMeters,
            geometryHeightMeters: null,
            footprintAreaSquareMeters: null);

        Assert.False(scale.LowRise);
        Assert.True(scale.MidOrHighRise);
    }

    [Theory]
    [InlineData(8, null)]
    [InlineData(null, 25.0)]
    [InlineData(19, 79.999)]
    public void ClassifyTreatsEightToNineteenFloorsOrTwentyFiveToUnderEightyMetersAsMidrise(
        int? floorCount,
        double? measuredHeightMeters)
    {
        BuildingFacadeScale scale = BuildingFacadeScale.Classify(
            floorCount,
            measuredHeightMeters,
            geometryHeightMeters: null,
            footprintAreaSquareMeters: null);

        Assert.True(scale.Midrise);
        Assert.False(scale.Highrise);
        Assert.False(scale.Landmark);
    }

    [Theory]
    [InlineData(20, null)]
    [InlineData(null, 80.0)]
    [InlineData(34, 149.999)]
    public void ClassifyTreatsTwentyToThirtyFourFloorsOrEightyToUnderOneHundredFiftyMetersAsHighrise(
        int? floorCount,
        double? measuredHeightMeters)
    {
        BuildingFacadeScale scale = BuildingFacadeScale.Classify(
            floorCount,
            measuredHeightMeters,
            geometryHeightMeters: null,
            footprintAreaSquareMeters: null);

        Assert.False(scale.Midrise);
        Assert.True(scale.Highrise);
        Assert.False(scale.Landmark);
    }

    [Theory]
    [InlineData(35, null)]
    [InlineData(null, 150.0)]
    public void ClassifyTreatsThirtyFiveFloorsOrOneHundredFiftyMetersAsLandmark(
        int? floorCount,
        double? measuredHeightMeters)
    {
        BuildingFacadeScale scale = BuildingFacadeScale.Classify(
            floorCount,
            measuredHeightMeters,
            geometryHeightMeters: null,
            footprintAreaSquareMeters: null);

        Assert.True(scale.Landmark);
    }

    [Fact]
    public void ClassifyFallsBackToGeometryHeightWhenMeasuredHeightIsNotUsable()
    {
        BuildingFacadeScale scale = BuildingFacadeScale.Classify(
            floorCount: null,
            measuredHeightMeters: double.NaN,
            geometryHeightMeters: 95.0,
            footprintAreaSquareMeters: null);

        Assert.True(scale.Highrise);
    }

    [Fact]
    public void ClassifyUsesGeometryHeightFallbackForLandmarkThreshold()
    {
        BuildingFacadeScale scale = BuildingFacadeScale.Classify(
            floorCount: null,
            measuredHeightMeters: double.NaN,
            geometryHeightMeters: 150.0,
            footprintAreaSquareMeters: null);

        Assert.True(scale.Landmark);
    }

    [Theory]
    [InlineData(999.999, false)]
    [InlineData(1000.0, true)]
    public void ClassifyMarksOnlyLargeLowRiseFootprintsAsLargeLowRise(double footprintAreaSquareMeters, bool expected)
    {
        BuildingFacadeScale scale = BuildingFacadeScale.Classify(
            floorCount: 3,
            measuredHeightMeters: 8.0,
            geometryHeightMeters: null,
            footprintAreaSquareMeters: footprintAreaSquareMeters);

        Assert.Equal(expected, scale.LargeLowRise);
    }
}
