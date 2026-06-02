using System;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class GeneratedRoadMarkingCityObjectFactoryTests
{
    [Fact]
    public void CreateGeneratesCenteredSegmentsForUntexturedTransportationQuad()
    {
        ParsedCityObject road = CreateRoadObject(
            packageName: "tran",
            surface: CreateRoadSurface(
                "road",
                width: 4.0,
                length: 12.0,
                texturePayload: null));

        ParsedCityObject? marking = GeneratedRoadMarkingCityObjectFactory.Create(road, new GeodeticPoint(0.0, 0.0, 0.0), cityObjectCartesian: null);

        Assert.NotNull(marking);
        Assert.Equal("road-slot_road_marking", marking.SlotKey);
        Assert.Equal("Road Marking", marking.DisplayName);
        Assert.Equal(3, marking.Surfaces.Length);
        Assert.All(marking.Surfaces, surface =>
        {
            Assert.StartsWith("road_generated_marking_", surface.PolygonId, StringComparison.Ordinal);
            Assert.Equal(new ColorRgba(1.0, 1.0, 1.0, 1.0), surface.BaseColor);
            Assert.Null(surface.TexturePayload);
            Assert.Empty(surface.InteriorRings);
        });

        GeodeticPoint[] firstVertices = marking.Surfaces[0].ExteriorRing.Vertices;
        Assert.Equal(0.0, firstVertices[0].Latitude, precision: 6);
        Assert.Equal(4.0, firstVertices[1].Latitude, precision: 6);
        Assert.Equal(1.925, firstVertices[0].Longitude, precision: 6);
        Assert.Equal(1.925, firstVertices[1].Longitude, precision: 6);
        Assert.Equal(2.075, firstVertices[2].Longitude, precision: 6);
        Assert.Equal(2.075, firstVertices[3].Longitude, precision: 6);
    }

    [Fact]
    public void CreateSkipsNarrowRoadSurface()
    {
        ParsedCityObject road = CreateRoadObject(
            packageName: "tran",
            surface: CreateRoadSurface(
                "narrow-road",
                width: 0.2,
                length: 12.0,
                texturePayload: null));

        ParsedCityObject? marking = GeneratedRoadMarkingCityObjectFactory.Create(road, new GeodeticPoint(0.0, 0.0, 0.0), cityObjectCartesian: null);

        Assert.Null(marking);
    }

    [Fact]
    public void CreateSkipsTexturedRoadSurface()
    {
        ParsedCityObject road = CreateRoadObject(
            packageName: "tran",
            surface: CreateRoadSurface(
                "textured-road",
                width: 4.0,
                length: 12.0,
                texturePayload: new RawRgba32TexturePayload(1, 1, "sRGB", [255, 255, 255, 255], "road-texture")));

        ParsedCityObject? marking = GeneratedRoadMarkingCityObjectFactory.Create(road, new GeodeticPoint(0.0, 0.0, 0.0), cityObjectCartesian: null);

        Assert.Null(marking);
    }

    [Fact]
    public void CreateGeneratesMarkingsOnlyForEligibleTransportationSurfaces()
    {
        ParsedCityObject road = CreateRoadObject(
            packageName: "tran",
            surfaces:
            [
                CreateRoadSurface(
                    "textured-road",
                    width: 4.0,
                    length: 12.0,
                    texturePayload: new RawRgba32TexturePayload(1, 1, "sRGB", [255, 255, 255, 255], "road-texture")),
                CreateRoadSurface(
                    "plain-road",
                    width: 4.0,
                    length: 12.0,
                    texturePayload: null),
            ]);

        ParsedCityObject? marking = GeneratedRoadMarkingCityObjectFactory.Create(road, new GeodeticPoint(0.0, 0.0, 0.0), cityObjectCartesian: null);

        Assert.NotNull(marking);
        Assert.All(marking.Surfaces, surface => Assert.StartsWith("plain-road_generated_marking_", surface.PolygonId, StringComparison.Ordinal));
    }

    [Fact]
    public void CreateSkipsNonTransportationObject()
    {
        ParsedCityObject building = CreateRoadObject(
            packageName: "bldg",
            surface: CreateRoadSurface(
                "building-ground",
                width: 4.0,
                length: 12.0,
                texturePayload: null));

        ParsedCityObject? marking = GeneratedRoadMarkingCityObjectFactory.Create(building, new GeodeticPoint(0.0, 0.0, 0.0), cityObjectCartesian: null);

        Assert.Null(marking);
    }

    [Fact]
    public void RoadSurfaceEdgePairSelectorRejectsNonQuadInputsWithClearPrecondition()
    {
        GeodeticPoint[] vertices =
        [
            new(0.0, 0.0, 0.0),
            new(1.0, 0.0, 0.0),
            new(1.0, 1.0, 0.0),
        ];
        Float3[] positions =
        [
            new(0.0, 0.0, 0.0),
            new(1.0, 0.0, 0.0),
            new(1.0, 0.0, 1.0),
        ];

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => RoadSurfaceEdgePairSelector.Select(vertices, positions));

        Assert.Equal("vertices", exception.ParamName);
        Assert.Contains("exactly four vertices", exception.Message, StringComparison.Ordinal);
    }

    private static ParsedCityObject CreateRoadObject(string packageName, ParsedSurface surface)
    {
        return CreateRoadObject(packageName, [surface]);
    }

    private static ParsedCityObject CreateRoadObject(string packageName, ParsedSurface[] surfaces)
    {
        return new ParsedCityObject(
            "road-slot",
            "Road",
            packageName,
            "53394525",
            LodLevel: 2,
            surfaces,
            new CoordinateReferenceSystem("local", Geocentric: null, CompatibilityKey: "local"),
            "udx/tran/road.gml",
            SharedAcrossMeshCodes: false,
            BuildingAttributes: BuildingAttributeContext.Empty,
            TerrainAligned: true);
    }

    private static ParsedSurface CreateRoadSurface(
        string polygonId,
        double width,
        double length,
        TexturePayload? texturePayload)
    {
        GeodeticPoint[] vertices =
        [
            new(0.0, 0.0, 0.0),
            new(length, 0.0, 0.0),
            new(length, width, 0.0),
            new(0.0, width, 0.0),
        ];

        return new ParsedSurface(
            polygonId,
            ParsedSurfaceSemantic.Ground,
            new ParsedRing($"{polygonId}-ring", vertices, UVs: null),
            InteriorRings: [],
            new ColorRgba(0.2, 0.2, 0.2, 1.0),
            texturePayload);
    }
}
