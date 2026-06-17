using System;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class CityObjectGeographicBoundsResolverTests
{
    [Fact]
    public void ResolveComputesLatitudeAndLongitudeExtents()
    {
        GeographicRectangle bounds = CityObjectGeographicBoundsResolver.Resolve(
            [
                new GeodeticPoint(35.2, 139.1, 20.0),
                new GeodeticPoint(35.0, 139.4, 10.0),
                new GeodeticPoint(35.1, 139.0, 30.0),
            ]);

        Assert.Equal(35.0, bounds.MinLatitude, 12);
        Assert.Equal(35.2, bounds.MaxLatitude, 12);
        Assert.Equal(139.0, bounds.MinLongitude, 12);
        Assert.Equal(139.4, bounds.MaxLongitude, 12);
    }

    [Fact]
    public void ResolveRejectsEmptyGeometry()
    {
        Assert.Throws<InvalidOperationException>(
            static () => CityObjectGeographicBoundsResolver.Resolve([]));
    }

    [Fact]
    public void ResolvePreservesEnumerableFloatingPointNaNBehavior()
    {
        GeographicRectangle bounds = CityObjectGeographicBoundsResolver.Resolve(
            [
                new GeodeticPoint(double.NaN, double.NaN, 0.0),
                new GeodeticPoint(35.2, 139.4, 0.0),
            ]);

        Assert.True(double.IsNaN(bounds.MinLatitude));
        Assert.Equal(35.2, bounds.MaxLatitude, 12);
        Assert.True(double.IsNaN(bounds.MinLongitude));
        Assert.Equal(139.4, bounds.MaxLongitude, 12);
    }
}
