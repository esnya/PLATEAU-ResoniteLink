using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class RoofTerrainTextureSurfacePolicyTests
{
    [Fact]
    public void IsRoofTerrainTextureSurfaceComputesUnknownSurfaceNormalFromExteriorRing()
    {
        ParsedSurface surface = new(
            PolygonId: "surface-with-hole",
            Semantic: ParsedSurfaceSemantic.Unknown,
            ExteriorRing: new ParsedRing(
                "surface-with-hole-exterior",
                [
                    new GeodeticPoint(35.0, 139.0, 10.0),
                    new GeodeticPoint(35.0, 139.1, 10.0),
                    new GeodeticPoint(35.1, 139.1, 10.0),
                    new GeodeticPoint(35.1, 139.0, 10.0),
                ],
                UVs: null),
            InteriorRings:
            [
                new ParsedRing(
                    "surface-with-hole-interior",
                    [
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
    public void IsRoofTerrainTextureSurfaceTreatsGeneratedNoWallWallSurfaceAsRoofSlab()
    {
        ParsedSurface surface = new(
            PolygonId: "roof_generated_no-wall-side-0",
            Semantic: ParsedSurfaceSemantic.Wall,
            ExteriorRing: new ParsedRing(
                "roof_generated_no-wall-side-0-ring",
                [
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
            new ConstructionFace(surface, ConstructionCityObjectDraft.ResolveRole(surface)),
            cityObjectMinAltitude: 0.0,
            cityObjectOrigin: new GeodeticPoint(35.0, 139.0, 0.0),
            cityObjectCartesian: null);

        Assert.True(result);
    }
}
