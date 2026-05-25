using System.Linq;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class GeneratedLod1RoofCityObjectFactoryTests
{
    [Fact]
    public void CreateReplacesSingleTexturelessTopSurfaceWithGeneratedRoofSurfaces()
    {
        ParsedSurface top = CreateSurface(
            "lod1-top",
            ParsedSurfaceSemantic.Roof,
            altitude: 10.0);
        ParsedSurface bottom = CreateSurface(
            "lod1-bottom",
            ParsedSurfaceSemantic.Ground,
            altitude: 0.0);
        ParsedCityObject cityObject = CreateCityObject(
            [top, bottom],
            CoordinateReferenceSystem.Parse("EPSG:6697"),
            BuildingAttributeContext.Empty with
            {
                RoofShape = new BuildingCodeValue<CityGmlRoofShape>(CityGmlRoofShape.Shed, "shed"),
            });

        ParsedCityObject generated = GeneratedLod1RoofCityObjectFactory.Create(cityObject);

        Assert.DoesNotContain(generated.Surfaces, static surface => surface.PolygonId == "lod1-top");
        Assert.Contains(generated.Surfaces, static surface => surface.PolygonId == "lod1-bottom");
        Assert.Equal(4, generated.Surfaces.Count(static surface => surface.PolygonId.Contains("_generated_", System.StringComparison.Ordinal)));
        Assert.All(
            generated.Surfaces.Where(static surface => surface.PolygonId.Contains("_generated_", System.StringComparison.Ordinal)),
            static surface => Assert.Null(surface.TexturePayload));
    }

    [Fact]
    public void CreateSkipsObjectThatAlreadyHasGeneratedRoofSurface()
    {
        ParsedCityObject cityObject = CreateCityObject(
            [
                CreateSurface("lod1-top_generated_shed-roof", ParsedSurfaceSemantic.Roof, altitude: 10.0),
                CreateSurface("lod1-bottom", ParsedSurfaceSemantic.Ground, altitude: 0.0),
            ],
            CoordinateReferenceSystem.Parse("EPSG:6697"),
            BuildingAttributeContext.Empty with
            {
                RoofShape = new BuildingCodeValue<CityGmlRoofShape>(CityGmlRoofShape.Shed, "shed"),
            });

        ParsedCityObject generated = GeneratedLod1RoofCityObjectFactory.Create(cityObject);

        Assert.Same(cityObject, generated);
    }

    [Fact]
    public void CreateSkipsObjectWhoseGeneratedRoofSurfaceIdContainsEarlierGeneratedToken()
    {
        ParsedCityObject cityObject = CreateCityObject(
            [
                CreateSurface("source_generated_part_generated_shed-roof", ParsedSurfaceSemantic.Roof, altitude: 10.0),
                CreateSurface("lod1-bottom", ParsedSurfaceSemantic.Ground, altitude: 0.0),
            ],
            CoordinateReferenceSystem.Parse("EPSG:6697"),
            BuildingAttributeContext.Empty with
            {
                RoofShape = new BuildingCodeValue<CityGmlRoofShape>(CityGmlRoofShape.Shed, "shed"),
            });

        ParsedCityObject generated = GeneratedLod1RoofCityObjectFactory.Create(cityObject);

        Assert.Same(cityObject, generated);
    }

    [Fact]
    public void CreateDoesNotTreatOtherGeneratedSurfaceIdsAsGeneratedRoof()
    {
        ParsedCityObject cityObject = CreateCityObject(
            [
                CreateSurface("lod1-top", ParsedSurfaceSemantic.Roof, altitude: 10.0),
                CreateSurface("lod1-bottom", ParsedSurfaceSemantic.Ground, altitude: 0.0),
                CreateSurface("tran_generated_marking", ParsedSurfaceSemantic.Wall, altitude: 1.0),
            ],
            CoordinateReferenceSystem.Parse("EPSG:6697"),
            BuildingAttributeContext.Empty with
            {
                RoofShape = new BuildingCodeValue<CityGmlRoofShape>(CityGmlRoofShape.Shed, "shed"),
            });

        ParsedCityObject generated = GeneratedLod1RoofCityObjectFactory.Create(cityObject);

        Assert.NotSame(cityObject, generated);
        Assert.Contains(generated.Surfaces, static surface => surface.PolygonId == "tran_generated_marking");
        Assert.Contains(generated.Surfaces, static surface => surface.PolygonId.Contains("_generated_shed-", System.StringComparison.Ordinal));
    }

    [Fact]
    public void CreateSkipsNonGeographicObject()
    {
        ParsedCityObject cityObject = CreateCityObject(
            [
                CreateSurface("lod1-top", ParsedSurfaceSemantic.Roof, altitude: 10.0),
                CreateSurface("lod1-bottom", ParsedSurfaceSemantic.Ground, altitude: 0.0),
            ],
            CoordinateReferenceSystem.Parse((string?)null),
            BuildingAttributeContext.Empty with
            {
                RoofShape = new BuildingCodeValue<CityGmlRoofShape>(CityGmlRoofShape.Shed, "shed"),
            });

        ParsedCityObject generated = GeneratedLod1RoofCityObjectFactory.Create(cityObject);

        Assert.Same(cityObject, generated);
    }

    private static ParsedCityObject CreateCityObject(
        ParsedSurface[] surfaces,
        CoordinateReferenceSystem referenceSystem,
        BuildingAttributeContext attributes)
    {
        return new ParsedCityObject(
            SlotKey: "bldg-lod1",
            DisplayName: "bldg-lod1",
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 1,
            Surfaces: surfaces,
            ReferenceSystem: referenceSystem,
            SourceFileRelativePath: "udx/bldg/53394525/bldg.gml",
            SharedAcrossMeshCodes: false,
            BuildingAttributes: attributes);
    }

    private static ParsedSurface CreateSurface(
        string polygonId,
        ParsedSurfaceSemantic semantic,
        double altitude)
    {
        GeodeticPoint[] vertices =
        [
            new(35.0, 139.0, altitude),
            new(35.0, 139.00020, altitude),
            new(35.00010, 139.00020, altitude),
            new(35.00010, 139.0, altitude),
            new(35.0, 139.0, altitude),
        ];
        return new ParsedSurface(
            polygonId,
            semantic,
            new ParsedRing($"{polygonId}-ring", vertices, UVs: null),
            InteriorRings: [],
            BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null);
    }
}
