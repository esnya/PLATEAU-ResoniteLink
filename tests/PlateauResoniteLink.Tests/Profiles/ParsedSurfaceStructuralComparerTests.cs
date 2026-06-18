using PlateauResoniteLink.Core.Application.Importing.Contracts;
using PlateauResoniteLink.Plateau.Application.Importing.Source;

using System.Collections.Generic;


namespace PlateauResoniteLink.Tests.Profiles;

public sealed class ParsedSurfaceStructuralComparerTests
{
    [Fact]
    public void CompareReturnsZeroForEqualGeometryAndMaterialInputs()
    {
        ParsedSurface first = CreateSurface([]);
        ParsedSurface second = CreateSurface([]);

        Assert.Equal(0, ParsedSurfaceStructuralComparer.Instance.Compare(first, second));
    }

    [Fact]
    public void CompareChangesWhenGeometryChanges()
    {
        ParsedSurface first = CreateSurface([]);
        ParsedSurface second = CreateSurface([], exteriorOffset: 0.001);

        Assert.NotEqual(0, ParsedSurfaceStructuralComparer.Instance.Compare(first, second));
    }

    [Fact]
    public void CompareChangesWhenTextureCoordinatesChange()
    {
        ParsedSurface first = CreateSurface([]);
        ParsedSurface second = CreateSurface([], uvOffset: 0.125);

        Assert.NotEqual(0, ParsedSurfaceStructuralComparer.Instance.Compare(first, second));
    }

    private static ParsedSurface CreateSurface(
        ParsedRing[] interiorRings,
        double exteriorOffset = 0.0,
        double uvOffset = 0.0)
    {
        return new ParsedSurface(
            ParsedSurfaceSemantic.Wall,
            CreateRing(exteriorOffset, uvOffset),
            interiorRings,
            new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null);
    }

    private static ParsedRing CreateRing(
        double coordinateOffset,
        double uvOffset = 0.0)
    {
        return new ParsedRing(
            [
                new GeodeticPoint(35.0 + coordinateOffset, 139.0, 8.0),
                new GeodeticPoint(35.0 + coordinateOffset, 139.001, 8.0),
                new GeodeticPoint(35.001 + coordinateOffset, 139.001, 8.0),
                new GeodeticPoint(35.001 + coordinateOffset, 139.0, 8.0),
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
