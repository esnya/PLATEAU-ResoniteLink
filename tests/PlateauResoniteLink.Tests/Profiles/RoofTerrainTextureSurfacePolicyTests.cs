using PlateauResoniteLink.Application.Importing.Contracts;
using PlateauResoniteLink.Application.Importing.Plateau;
using PlateauResoniteLink.Application.Importing.Source;


namespace PlateauResoniteLink.Tests.Profiles;

public sealed class RoofTerrainTextureSurfacePolicyTests
{
    [Fact]
    public void IsRoofTerrainTextureSurfaceComputesUnknownSurfaceNormalFromExteriorRing()
    {
        ParsedSurface surface = new(
            Semantic: ParsedSurfaceSemantic.Unknown,
            ExteriorRing: new ParsedRing([
                    new GeodeticPoint(35.0, 139.0, 10.0),
                    new GeodeticPoint(35.0, 139.1, 10.0),
                    new GeodeticPoint(35.1, 139.1, 10.0),
                    new GeodeticPoint(35.1, 139.0, 10.0),
                ],
                UVs: null),
            InteriorRings:
            [
                new ParsedRing([
                        new GeodeticPoint(35.02, 139.02, 10.0),
                        new GeodeticPoint(35.08, 139.02, 80.0),
                        new GeodeticPoint(35.08, 139.08, 10.0),
                        new GeodeticPoint(35.02, 139.08, 80.0),
                    ],
                    UVs: null),
            ],
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null);

        bool result = RoofTerrainTextureSurfacePolicy.IsRoofTerrainTextureSurface(
            new ConstructionFace(surface, ConstructionCityObjectDraft.ResolveRole(surface)),
            cityObjectMinAltitude: 0.0,
            cityObjectOrigin: new GeodeticPoint(35.0, 139.0, 0.0),
            cityObjectCartesian: null);

        Assert.True(result);
    }

    [Fact]
    public void IsRoofTerrainTextureSurfaceTreatsRoofSlabWallSurfaceAsTerrainTextureSurface()
    {
        ParsedSurface surface = new(
            Semantic: ParsedSurfaceSemantic.Wall,
            ExteriorRing: new ParsedRing([
                    new GeodeticPoint(35.0, 139.0, 10.0),
                    new GeodeticPoint(35.0, 139.0, 9.7),
                    new GeodeticPoint(35.0, 139.1, 9.7),
                    new GeodeticPoint(35.0, 139.1, 10.0),
                ],
                UVs: null),
            InteriorRings: [],
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null);

        bool result = RoofTerrainTextureSurfacePolicy.IsRoofTerrainTextureSurface(
            new ConstructionFace(surface, ConstructionFaceRole.RoofSlab),
            cityObjectMinAltitude: 0.0,
            cityObjectOrigin: new GeodeticPoint(35.0, 139.0, 0.0),
            cityObjectCartesian: null);

        Assert.True(result);
    }
}
