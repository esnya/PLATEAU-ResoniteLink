using PlateauResoniteLink.Application.Importing;

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
}
