using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Domain;

public sealed class PlateauMeshCodeTests
{
    [Fact]
    public void TryGetBoundsReturnsExpectedBoundsForValidThirdLevelMesh()
    {
        bool parsed = PlateauMeshCode.TryGetBounds(
            "53394525",
            out (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) bounds);

        Assert.True(parsed);
        Assert.Equal(35.683333333333337, bounds.SouthLatitude, 9);
        Assert.Equal(35.69166666666667, bounds.NorthLatitude, 9);
        Assert.Equal(139.6875, bounds.WestLongitude, 9);
        Assert.Equal(139.7, bounds.EastLongitude, 9);
    }

    [Fact]
    public void TryGetCenterReturnsExpectedCenterForValidThirdLevelMesh()
    {
        bool parsed = PlateauMeshCode.TryGetCenter("53394525", out ResoniteLocalOrigin center);

        Assert.True(parsed);
        Assert.Equal(35.6875, center.Latitude, 9);
        Assert.Equal(139.69375, center.Longitude, 9);
        Assert.Equal(0.0, center.Altitude, 9);
    }

    [Theory]
    [InlineData("")]
    [InlineData("5339452A")]
    [InlineData("5339452")]
    [InlineData("533948")]
    public void TryGetBoundsRejectsInvalidMeshCodes(string meshCode)
    {
        bool parsed = PlateauMeshCode.TryGetBounds(
            meshCode,
            out (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) _);

        Assert.False(parsed);
    }
}
