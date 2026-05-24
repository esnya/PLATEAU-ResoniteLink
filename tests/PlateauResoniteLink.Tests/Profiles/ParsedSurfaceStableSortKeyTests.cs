using System.Collections.Generic;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class ParsedSurfaceStableSortKeyTests
{
    [Fact]
    public void CreateIsIndependentOfInteriorRingInputOrder()
    {
        LocalCityGmlObjectProjection.ParsedRing ringA = CreateRing("a", 0.001);
        LocalCityGmlObjectProjection.ParsedRing ringB = CreateRing("b", 0.002);

        LocalCityGmlObjectProjection.ParsedSurface first = CreateSurface([ringB, ringA]);
        LocalCityGmlObjectProjection.ParsedSurface second = CreateSurface([ringA, ringB]);

        Assert.Equal(ParsedSurfaceStableSortKey.Create(first), ParsedSurfaceStableSortKey.Create(second));
    }

    [Fact]
    public void CreateChangesWhenGeometryChanges()
    {
        LocalCityGmlObjectProjection.ParsedSurface first = CreateSurface([]);
        LocalCityGmlObjectProjection.ParsedSurface second = CreateSurface([], exteriorOffset: 0.001);

        Assert.NotEqual(ParsedSurfaceStableSortKey.Create(first), ParsedSurfaceStableSortKey.Create(second));
    }

    [Fact]
    public void CreateChangesWhenTextureCoordinatesChange()
    {
        LocalCityGmlObjectProjection.ParsedSurface first = CreateSurface([]);
        LocalCityGmlObjectProjection.ParsedSurface second = CreateSurface([], uvOffset: 0.125);

        Assert.NotEqual(ParsedSurfaceStableSortKey.Create(first), ParsedSurfaceStableSortKey.Create(second));
    }

    private static LocalCityGmlObjectProjection.ParsedSurface CreateSurface(
        LocalCityGmlObjectProjection.ParsedRing[] interiorRings,
        double exteriorOffset = 0.0,
        double uvOffset = 0.0)
    {
        return new LocalCityGmlObjectProjection.ParsedSurface(
            "polygon",
            LocalCityGmlObjectProjection.ParsedSurfaceSemantic.Wall,
            CreateRing("exterior", exteriorOffset, uvOffset),
            interiorRings,
            new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null);
    }

    private static LocalCityGmlObjectProjection.ParsedRing CreateRing(
        string ringId,
        double coordinateOffset,
        double uvOffset = 0.0)
    {
        return new LocalCityGmlObjectProjection.ParsedRing(
            ringId,
            [
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0 + coordinateOffset, 139.0, 8.0),
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0 + coordinateOffset, 139.001, 8.0),
                new LocalCityGmlObjectProjection.GeodeticPoint(35.001 + coordinateOffset, 139.001, 8.0),
                new LocalCityGmlObjectProjection.GeodeticPoint(35.001 + coordinateOffset, 139.0, 8.0),
            ],
            new List<Float2>
            {
                new(0.0 + uvOffset, 0.0),
                new(1.0 + uvOffset, 0.0),
                new(1.0 + uvOffset, 1.0),
                new(0.0 + uvOffset, 1.0),
            });
    }
}
