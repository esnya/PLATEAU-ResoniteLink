using PlateauResoniteLink.Application.Importing.Source;

using System;


namespace PlateauResoniteLink.Tests.Profiles;

public sealed class CityObjectOriginResolverTests
{
    [Fact]
    public void ResolveUsesOriginOverride()
    {
        GeodeticPoint overrideOrigin = new(36.0, 140.0, 12.0);

        GeodeticPoint origin = CityObjectOriginResolver.Resolve(
            overrideOrigin,
            CreateVertices());

        Assert.Equal(overrideOrigin, origin);
    }

    [Fact]
    public void ResolveComputesBoundingCenterAtMinimumAltitude()
    {
        GeodeticPoint origin = CityObjectOriginResolver.Resolve(
            originOverride: null,
            CreateVertices(
                new GeodeticPoint(35.0, 139.0, 5.0),
                new GeodeticPoint(35.2, 139.4, 7.0),
                new GeodeticPoint(35.1, 139.1, 3.0)));

        Assert.Equal(35.1, origin.Latitude, 12);
        Assert.Equal(139.2, origin.Longitude, 12);
        Assert.Equal(3.0, origin.Altitude, 12);
    }

    [Fact]
    public void ResolveRejectsEmptyGeometry()
    {
        Assert.Throws<InvalidOperationException>(
            static () => CityObjectOriginResolver.Resolve(
                originOverride: null,
                []));
    }

    [Fact]
    public void ResolvePreservesEnumerableFloatingPointNaNBehavior()
    {
        GeodeticPoint origin = CityObjectOriginResolver.Resolve(
            originOverride: null,
            CreateVertices(
                new GeodeticPoint(double.NaN, double.NaN, 12.0),
                new GeodeticPoint(35.2, 139.4, double.NaN),
                new GeodeticPoint(35.0, 139.0, 3.0)));

        Assert.True(double.IsNaN(origin.Latitude));
        Assert.True(double.IsNaN(origin.Longitude));
        Assert.True(double.IsNaN(origin.Altitude));
    }

    [Fact]
    public void ResolveSupportsSelectorInput()
    {
        TestPoint origin = CityObjectOriginResolver.Resolve(
            originOverride: null,
            [
                new TestPoint(35.0, 139.0, 5.0),
                new TestPoint(35.2, 139.4, 7.0),
                new TestPoint(35.1, 139.1, 3.0),
            ],
            static point => point.Latitude,
            static point => point.Longitude,
            static point => point.Altitude,
            static (latitude, longitude, altitude) => new TestPoint(latitude, longitude, altitude));

        Assert.Equal(new TestPoint(35.1, 139.2, 3.0), origin);
    }

    private static GeodeticPoint[] CreateVertices(params GeodeticPoint[] vertices)
    {
        if (vertices.Length > 0)
        {
            return vertices;
        }

        return
        [
            new GeodeticPoint(35.0, 139.0, 0.0),
            new GeodeticPoint(35.0, 139.1, 0.0),
            new GeodeticPoint(35.1, 139.1, 0.0),
        ];
    }

    private sealed record TestPoint(double Latitude, double Longitude, double Altitude);
}
