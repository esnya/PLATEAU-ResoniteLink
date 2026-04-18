using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class DemTerrainOverlayAssignmentTests
{
    [Fact]
    public void SplitParsedCityObjectCollapsesCentimeterClassBoundarySliverToDominantOverlay()
    {
        const double boundaryLongitude = 139.0100;
        LocalCityGmlObjectProjection.ParsedSurface surface = CreateGeneratedSurface(
            "dem-sliver",
            [
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0000, 139.0000, 0.0),
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, 139.0000, 1.0),
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, boundaryLongitude + 0.0000005, 2.0),
            ]);
        LocalCityGmlObjectProjection.ParsedCityObject cityObject = CreateCityObject(surface);
        TerrainTextureOverlay[] overlays =
        [
            CreateOverlay(139.0000, boundaryLongitude),
            CreateOverlay(boundaryLongitude, 139.0200),
        ];

        (LocalCityGmlObjectProjection.ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)[] results =
            DemTerrainOverlayAssignment.SplitParsedCityObject(cityObject, overlays).ToArray();

        (LocalCityGmlObjectProjection.ParsedCityObject splitCityObject, TerrainTextureOverlay? overlay) = Assert.Single(results);
        Assert.NotNull(overlay);
        Assert.Equal(139.0000, overlay.GeographicBounds.MinLongitude, 6);
        Assert.Equal(boundaryLongitude, overlay.GeographicBounds.MaxLongitude, 6);

        LocalCityGmlObjectProjection.ParsedSurface collapsedSurface = Assert.Single(splitCityObject.Surfaces);
        Assert.Equal(surface.PolygonId, collapsedSurface.PolygonId);
        Assert.Equal(surface.ExteriorRing.Vertices, collapsedSurface.ExteriorRing.Vertices);
        Assert.True(collapsedSurface.UsesGeneratedDemTexture);
    }

    [Fact]
    public void SplitParsedCityObjectKeepsMeaningfulBoundarySplit()
    {
        const double boundaryLongitude = 139.0100;
        LocalCityGmlObjectProjection.ParsedSurface surface = CreateGeneratedSurface(
            "dem-wide-split",
            [
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0000, 139.0000, 0.0),
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, 139.0000, 1.0),
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, 139.0120, 2.0),
            ]);
        LocalCityGmlObjectProjection.ParsedCityObject cityObject = CreateCityObject(surface);
        TerrainTextureOverlay[] overlays =
        [
            CreateOverlay(139.0000, boundaryLongitude),
            CreateOverlay(boundaryLongitude, 139.0200),
        ];

        (LocalCityGmlObjectProjection.ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)[] results =
            DemTerrainOverlayAssignment.SplitParsedCityObject(cityObject, overlays).ToArray();

        Assert.Equal(2, results.Length);
        Assert.All(results, static result => Assert.Single(result.CityObject.Surfaces));
        Assert.Contains(
            results,
            static result => result.Overlay is not null
                && result.Overlay.GeographicBounds.MinLongitude == 139.0000
                && result.Overlay.GeographicBounds.MaxLongitude == boundaryLongitude);
        Assert.Contains(
            results,
            static result => result.Overlay is not null
                && result.Overlay.GeographicBounds.MinLongitude == boundaryLongitude
                && result.Overlay.GeographicBounds.MaxLongitude == 139.0200);
    }

    [Fact]
    public void SplitParsedCityObjectCollapsesMultipleThinBoundarySliversToDominantOverlay()
    {
        const double boundaryOne = 139.0100;
        const double boundaryTwo = 139.0100003;
        const double boundaryThree = 139.0100006;
        LocalCityGmlObjectProjection.ParsedSurface surface = CreateGeneratedSurface(
            "dem-many-slivers",
            [
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0000, 139.0000, 0.0),
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, 139.0000, 1.0),
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, boundaryThree + 0.0000002, 2.0),
            ]);
        LocalCityGmlObjectProjection.ParsedCityObject cityObject = CreateCityObject(surface);
        TerrainTextureOverlay[] overlays =
        [
            CreateOverlay(139.0000, boundaryOne),
            CreateOverlay(boundaryOne, boundaryTwo),
            CreateOverlay(boundaryTwo, boundaryThree),
            CreateOverlay(boundaryThree, 139.0200),
        ];

        (LocalCityGmlObjectProjection.ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)[] results =
            DemTerrainOverlayAssignment.SplitParsedCityObject(cityObject, overlays).ToArray();

        (LocalCityGmlObjectProjection.ParsedCityObject splitCityObject, TerrainTextureOverlay? overlay) = Assert.Single(results);
        Assert.NotNull(overlay);
        Assert.Equal(139.0000, overlay.GeographicBounds.MinLongitude, 6);
        Assert.Equal(boundaryOne, overlay.GeographicBounds.MaxLongitude, 6);
        Assert.Single(splitCityObject.Surfaces);
    }

    [Fact]
    public void SplitParsedCityObjectFallsBackToNearestOverlayWhenSurfaceMissesOverlayBounds()
    {
        LocalCityGmlObjectProjection.ParsedSurface surface = CreateGeneratedSurface(
            "dem-nearest-overlay",
            [
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0000, 139.0200002, 0.0),
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, 139.0200002, 1.0),
                new LocalCityGmlObjectProjection.GeodeticPoint(35.0100, 139.0200006, 2.0),
            ]);
        LocalCityGmlObjectProjection.ParsedCityObject cityObject = CreateCityObject(surface);
        TerrainTextureOverlay[] overlays =
        [
            CreateOverlay(139.0000, 139.0100),
            CreateOverlay(139.0100, 139.0200),
        ];

        (LocalCityGmlObjectProjection.ParsedCityObject CityObject, TerrainTextureOverlay? Overlay)[] results =
            DemTerrainOverlayAssignment.SplitParsedCityObject(cityObject, overlays).ToArray();

        (LocalCityGmlObjectProjection.ParsedCityObject splitCityObject, TerrainTextureOverlay? overlay) = Assert.Single(results);
        Assert.NotNull(overlay);
        Assert.Equal(139.0100, overlay.GeographicBounds.MinLongitude, 6);
        Assert.Equal(139.0200, overlay.GeographicBounds.MaxLongitude, 6);
        Assert.True(Assert.Single(splitCityObject.Surfaces).UsesGeneratedDemTexture);
    }

    private static LocalCityGmlObjectProjection.ParsedCityObject CreateCityObject(
        LocalCityGmlObjectProjection.ParsedSurface surface)
    {
        return new LocalCityGmlObjectProjection.ParsedCityObject(
            SlotKey: "dem-object",
            DisplayName: "DEM Object",
            PackageName: "dem",
            ActualMeshCode: "53394525",
            LodLevel: 1,
            Surfaces: [surface],
            ReferenceSystem: LocalCityGmlObjectProjection.CoordinateReferenceSystem.Parse("EPSG:4326"),
            SourceFileRelativePath: "udx/dem/53394525/sample.gml",
            SourceUnitIdentity: "source-unit",
            SourceIdentity: "source",
            SharedAcrossMeshCodes: false);
    }

    private static LocalCityGmlObjectProjection.ParsedSurface CreateGeneratedSurface(
        string polygonId,
        LocalCityGmlObjectProjection.GeodeticPoint[] vertices)
    {
        return new LocalCityGmlObjectProjection.ParsedSurface(
            PolygonId: polygonId,
            Semantic: LocalCityGmlObjectProjection.ParsedSurfaceSemantic.Ground,
            ExteriorRing: new LocalCityGmlObjectProjection.ParsedRing(
                $"{polygonId}-ring",
                vertices,
                UVs: null),
            InteriorRings: [],
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null,
            UsesGeneratedDemTexture: true);
    }

    private static TerrainTextureOverlay CreateOverlay(double westLongitude, double eastLongitude)
    {
        return new TerrainTextureOverlay(
            PackageName: "dem",
            UrlTemplate: "https://tiles.example/{z}/{x}/{y}.png",
            ZoomLevel: 18,
            GeographicBounds: new GeographicRectangle(
                MinLatitude: 35.0000,
                MaxLatitude: 35.0200,
                MinLongitude: westLongitude,
                MaxLongitude: eastLongitude),
            MaxTextureSize: LocalCityGmlObjectProjection.DefaultDemTerrainTextureMaxSize);
    }

}
