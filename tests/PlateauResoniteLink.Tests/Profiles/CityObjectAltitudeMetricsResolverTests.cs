using System;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class CityObjectAltitudeMetricsResolverTests
{
    [Fact]
    public void GetMinimumAltitudeReturnsLowestVertexAltitude()
    {
        double minAltitude = CityObjectAltitudeMetricsResolver.GetMinimumAltitude(
            [
                new GeodeticPoint(35.0, 139.0, 12.0),
                new GeodeticPoint(35.1, 139.1, -2.5),
                new GeodeticPoint(35.2, 139.2, 8.0),
            ]);

        Assert.Equal(-2.5, minAltitude, 12);
    }

    [Fact]
    public void GetMinimumAltitudeRejectsEmptyGeometry()
    {
        Assert.Throws<InvalidOperationException>(
            static () => CityObjectAltitudeMetricsResolver.GetMinimumAltitude([]));
    }

    [Fact]
    public void GetMinimumAltitudePreservesEnumerableFloatingPointNaNBehavior()
    {
        double minAltitude = CityObjectAltitudeMetricsResolver.GetMinimumAltitude(
            [
                new GeodeticPoint(35.0, 139.0, 12.0),
                new GeodeticPoint(35.1, 139.1, double.NaN),
                new GeodeticPoint(35.2, 139.2, -2.5),
            ]);

        Assert.True(double.IsNaN(minAltitude));
    }

    [Fact]
    public void TryGetGeometryHeightMetersReturnsPositiveAltitudeSpan()
    {
        double? height = CityObjectAltitudeMetricsResolver.TryGetGeometryHeightMeters(
            [
                new GeodeticPoint(35.0, 139.0, 12.0),
                new GeodeticPoint(35.1, 139.1, -2.5),
                new GeodeticPoint(35.2, 139.2, 8.0),
            ]);

        Assert.Equal(14.5, height!.Value, 12);
    }

    [Fact]
    public void TryGetGeometryHeightMetersReturnsNullForFlatOrEmptyGeometry()
    {
        Assert.Null(CityObjectAltitudeMetricsResolver.TryGetGeometryHeightMeters([]));
        Assert.Null(CityObjectAltitudeMetricsResolver.TryGetGeometryHeightMeters(
            [
                new GeodeticPoint(35.0, 139.0, 12.0),
                new GeodeticPoint(35.1, 139.1, 12.0),
            ]));
    }

    [Fact]
    public void TryGetGeometryHeightMetersPreservesMathNaNBehavior()
    {
        Assert.Null(CityObjectAltitudeMetricsResolver.TryGetGeometryHeightMeters(
            [
                new GeodeticPoint(35.0, 139.0, 12.0),
                new GeodeticPoint(35.1, 139.1, double.NaN),
                new GeodeticPoint(35.2, 139.2, -2.5),
            ]));
    }
}
