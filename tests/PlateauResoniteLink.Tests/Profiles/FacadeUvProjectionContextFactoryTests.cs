using System;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class FacadeUvProjectionContextFactoryTests
{
    [Fact]
    public void TryCreateUsesNonGeneratedSurfacesForVerticalRange()
    {
        GeodeticPoint origin = new(35.0, 139.0, 0.0);
        GeographicLib.LocalCartesian cartesian = new(origin.Latitude, origin.Longitude, origin.Altitude);
        ParsedSurface wall = CreateSurface(
            "wall",
            minAltitude: 2.0,
            maxAltitude: 8.0);
        ParsedSurface generatedRoof = CreateSurface(
            "roof_generated_gable-top",
            minAltitude: 8.0,
            maxAltitude: 10.0);

        FacadeUvProjectionContext? context = FacadeUvProjectionContextFactory.TryCreate(
            "bldg",
            [wall, generatedRoof],
            origin,
            cartesian);

        Assert.NotNull(context);
        Assert.InRange(context.Value.MinimumY, 1.99999, 2.00001);
        Assert.InRange(context.Value.MaximumY, 7.99999, 8.00001);
        Assert.Equal(2, context.Value.FloorCount);
        Assert.InRange(context.Value.FloorHeightMeters, 2.99999, 3.00001);
    }

    [Fact]
    public void TryCreateFallsBackToAllSurfacesWhenNonGeneratedRangeIsFlat()
    {
        GeodeticPoint origin = new(35.0, 139.0, 0.0);
        GeographicLib.LocalCartesian cartesian = new(origin.Latitude, origin.Longitude, origin.Altitude);
        ParsedSurface flatWall = CreatePointSurface(
            "flat-wall",
            altitude: 5.0);
        ParsedSurface generatedRoof = CreateSurface(
            "roof_generated_gable-top",
            minAltitude: 2.0,
            maxAltitude: 9.0);

        FacadeUvProjectionContext? context = FacadeUvProjectionContextFactory.TryCreate(
            "bldg",
            [flatWall, generatedRoof],
            origin,
            cartesian);

        Assert.NotNull(context);
        Assert.InRange(context.Value.MinimumY, 1.99999, 2.00001);
        Assert.InRange(context.Value.MaximumY, 8.99999, 9.00001);
    }

    [Fact]
    public void TryCreateReturnsNullForNonBuildingPackages()
    {
        GeodeticPoint origin = new(35.0, 139.0, 0.0);

        FacadeUvProjectionContext? context = FacadeUvProjectionContextFactory.TryCreate(
            "tran",
            [CreateSurface("wall", minAltitude: 0.0, maxAltitude: 4.0)],
            origin,
            cityObjectCartesian: null);

        Assert.Null(context);
    }

    private static ParsedSurface CreateSurface(
        string polygonId,
        double minAltitude,
        double maxAltitude)
    {
        GeodeticPoint origin = new(35.0, 139.0, minAltitude);
        double longitudeDelta = 4.0 / (111320.0 * Math.Cos(origin.Latitude * (Math.PI / 180.0)));
        GeodeticPoint[] vertices =
        [
            origin,
            new(origin.Latitude, origin.Longitude + longitudeDelta, minAltitude),
            new(origin.Latitude, origin.Longitude + longitudeDelta, maxAltitude),
            new(origin.Latitude, origin.Longitude, maxAltitude),
            origin,
        ];
        return new ParsedSurface(
            polygonId,
            ParsedSurfaceSemantic.Wall,
            new ParsedRing($"{polygonId}-ring", vertices, UVs: null),
            InteriorRings: [],
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null);
    }

    private static ParsedSurface CreatePointSurface(
        string polygonId,
        double altitude)
    {
        GeodeticPoint point = new(35.0, 139.0, altitude);
        return new ParsedSurface(
            polygonId,
            ParsedSurfaceSemantic.Wall,
            new ParsedRing($"{polygonId}-ring", [point], UVs: null),
            InteriorRings: [],
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null);
    }
}
