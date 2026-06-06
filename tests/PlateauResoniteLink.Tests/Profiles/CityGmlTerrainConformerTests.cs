using GeographicLib;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class CityGmlTerrainConformerTests
{
    [Fact]
    public void ShouldTerrainAlignIncludesRoadLodBelowThreeAndTerrainLikePackages()
    {
        Assert.True(CityGmlTerrainConformer.ShouldTerrainAlign("tran", lodLevel: 2));
        Assert.False(CityGmlTerrainConformer.ShouldTerrainAlign("tran", lodLevel: 3));
        Assert.True(CityGmlTerrainConformer.ShouldTerrainAlign("luse", lodLevel: 1));
        Assert.False(CityGmlTerrainConformer.ShouldTerrainAlign("bldg", lodLevel: 1));
    }

    [Fact]
    public void ConformInterpolatesUnsampledNonRoadRingVerticesBetweenSampledNeighbors()
    {
        GeodeticPoint point0 = new(35.0, 139.0, 0.0);
        GeodeticPoint point1 = new(35.0001, 139.0, 0.0);
        GeodeticPoint point2 = new(35.0001, 139.0001, 0.0);
        GeodeticPoint point3 = new(35.0, 139.0001, 0.0);
        ParsedCityObject cityObject = CreateCityObject(
            "luse",
            [
                CreateSurface("ground", [point0, point1, point2, point3]),
            ]);
        ProjectionTerrainHeightSampler sampler = ProjectionTerrainHeightSampler.Create(
            [
                new ProjectionTerrainHeightTriangle(
                    new GeodeticPoint(point0.Latitude, point0.Longitude, 10.0),
                    new GeodeticPoint(point1.Latitude, point1.Longitude, 20.0),
                    new GeodeticPoint(point2.Latitude, point2.Longitude, 30.0)),
            ],
            new GeodeticPoint(point0.Latitude, point0.Longitude, point0.Altitude),
            Geocentric.WGS84);

        TerrainConformanceResult result = CityGmlTerrainConformer.Conform(
            cityObject,
            sampler,
            point0,
            new LocalCartesian(point0.Latitude, point0.Longitude, point0.Altitude, Geocentric.WGS84));

        GeodeticPoint[] vertices = result.Surfaces[0].ExteriorRing.Vertices;
        Assert.True(result.TerrainAligned);
        Assert.Equal(10.0, vertices[0].Altitude, precision: 6);
        Assert.Equal(20.0, vertices[1].Altitude, precision: 6);
        Assert.Equal(30.0, vertices[2].Altitude, precision: 6);
        Assert.Equal(20.0, vertices[3].Altitude, precision: 6);
    }

    private static ParsedCityObject CreateCityObject(string packageName, ParsedSurface[] surfaces)
    {
        return new ParsedCityObject(
            SlotKey: $"{packageName}-slot",
            DisplayName: packageName,
            PackageName: packageName,
            ActualMeshCode: "53394525",
            LodLevel: 1,
            Surfaces: surfaces,
            ReferenceSystem: CoordinateReferenceSystem.Parse("EPSG:4979"),
            SourceFileRelativePath: $"udx/{packageName}/53394525/{packageName}.gml",
            SharedAcrossMeshCodes: false,
            BuildingAttributes: BuildingAttributeContext.Empty,
            TerrainAligned: false);
    }

    private static ParsedSurface CreateSurface(string polygonId, GeodeticPoint[] vertices)
    {
        return new ParsedSurface(ParsedSurfaceSemantic.Ground,
            new ParsedRing(vertices, UVs: null),
            InteriorRings: [],
            BaseColor: new ColorRgba(0.5, 0.5, 0.5, 1.0),
            TexturePayload: null);
    }
}
