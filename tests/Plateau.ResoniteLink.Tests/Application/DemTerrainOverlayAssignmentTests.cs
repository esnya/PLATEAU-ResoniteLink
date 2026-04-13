using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class DemTerrainOverlayAssignmentTests
{
    [Fact]
    public void SplitParsedCityObjectCollapsesCentimeterClassBoundarySliverToDominantOverlay()
    {
        const double boundaryLongitude = 139.0100;
        LocalCityGmlResonitePlanBuilder.ParsedSurface surface = CreateSurface(
            "dem-sliver",
            [
                new LocalCityGmlResonitePlanBuilder.GeodeticPoint(35.0000, 139.0000, 0.0),
                new LocalCityGmlResonitePlanBuilder.GeodeticPoint(35.0100, 139.0000, 1.0),
                new LocalCityGmlResonitePlanBuilder.GeodeticPoint(35.0100, boundaryLongitude + 0.0000005, 2.0),
            ]);
        LocalCityGmlResonitePlanBuilder.ParsedCityObject cityObject = CreateCityObject(surface);
        TerrainTextureOverlay[] overlays =
        [
            CreateOverlay("west", 139.0000, boundaryLongitude),
            CreateOverlay("east", boundaryLongitude, 139.0200),
        ];

        (LocalCityGmlResonitePlanBuilder.ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)[] results =
            DemTerrainOverlayAssignment.SplitParsedCityObject(cityObject, overlays).ToArray();

        (LocalCityGmlResonitePlanBuilder.ParsedCityObject splitCityObject, TerrainTextureOverlay? overlay) = Assert.Single(results);
        Assert.Equal("terrain://dem/plateau-ortho/west", overlay!.TexturePath);
        LocalCityGmlResonitePlanBuilder.ParsedSurface collapsedSurface = Assert.Single(splitCityObject.Surfaces);
        Assert.Equal(surface.PolygonId, collapsedSurface.PolygonId);
        Assert.Equal(surface.ExteriorRing.Vertices, collapsedSurface.ExteriorRing.Vertices);
        Assert.Equal("terrain://dem/plateau-ortho/west", splitCityObject.Surfaces[0].TexturePath);
    }

    [Fact]
    public void SplitParsedCityObjectKeepsMeaningfulBoundarySplit()
    {
        const double boundaryLongitude = 139.0100;
        LocalCityGmlResonitePlanBuilder.ParsedSurface surface = CreateSurface(
            "dem-wide-split",
            [
                new LocalCityGmlResonitePlanBuilder.GeodeticPoint(35.0000, 139.0000, 0.0),
                new LocalCityGmlResonitePlanBuilder.GeodeticPoint(35.0100, 139.0000, 1.0),
                new LocalCityGmlResonitePlanBuilder.GeodeticPoint(35.0100, 139.0120, 2.0),
            ]);
        LocalCityGmlResonitePlanBuilder.ParsedCityObject cityObject = CreateCityObject(surface);
        TerrainTextureOverlay[] overlays =
        [
            CreateOverlay("west", 139.0000, boundaryLongitude),
            CreateOverlay("east", boundaryLongitude, 139.0200),
        ];

        (LocalCityGmlResonitePlanBuilder.ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)[] results =
            DemTerrainOverlayAssignment.SplitParsedCityObject(cityObject, overlays).ToArray();

        Assert.Equal(2, results.Length);
        Assert.Contains(results, static result => result.Overlay?.TexturePath == "terrain://dem/plateau-ortho/west");
        Assert.Contains(results, static result => result.Overlay?.TexturePath == "terrain://dem/plateau-ortho/east");
    }

    private static LocalCityGmlResonitePlanBuilder.ParsedCityObject CreateCityObject(
        LocalCityGmlResonitePlanBuilder.ParsedSurface surface)
    {
        return new LocalCityGmlResonitePlanBuilder.ParsedCityObject(
            SlotKey: "dem-object",
            DisplayName: "DEM Object",
            PackageName: "dem",
            ActualMeshCode: "53394525",
            LodLevel: 1,
            Surfaces: [surface],
            ReferenceSystem: LocalCityGmlResonitePlanBuilder.CoordinateReferenceSystem.Parse("EPSG:4326"),
            SourceUnitIdentity: "source-unit",
            SourceIdentity: "source",
            SharedAcrossMeshCodes: false);
    }

    private static LocalCityGmlResonitePlanBuilder.ParsedSurface CreateSurface(
        string polygonId,
        LocalCityGmlResonitePlanBuilder.GeodeticPoint[] vertices)
    {
        return new LocalCityGmlResonitePlanBuilder.ParsedSurface(
            PolygonId: polygonId,
            Semantic: LocalCityGmlResonitePlanBuilder.ParsedSurfaceSemantic.Ground,
            ExteriorRing: new LocalCityGmlResonitePlanBuilder.ParsedRing(
                $"{polygonId}-ring",
                vertices,
                UVs: null),
            InteriorRings: [],
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            TexturePath: LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTexturePath);
    }

    private static TerrainTextureOverlay CreateOverlay(string suffix, double westLongitude, double eastLongitude)
    {
        return new TerrainTextureOverlay(
            TexturePath: $"terrain://dem/plateau-ortho/{suffix}",
            PackageName: "dem",
            UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
            ZoomLevel: 18,
            GeographicBounds: new GeographicRectangle(
                MinLatitude: 35.0000,
                MaxLatitude: 35.0200,
                MinLongitude: westLongitude,
                MaxLongitude: eastLongitude),
            MaxTextureSize: LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTextureMaxSize);
    }
}
