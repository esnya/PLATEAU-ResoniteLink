using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class LocalCityGmlDemBootstrapSupportTests
{
    [Fact]
    public void AggregateDemParsedSourceFilesCombinesCachedFilesAndTriangles()
    {
        SourceFileDescriptor sourceFile = new(
            "udx/dem/53394525/sample.gml",
            "dem",
            "53394525",
            RequiresMeshAreaFilter: false);

        BootstrapParsedCityObject cityObject = CreateCityObject();
        ParsedSourceFileResult parsedWithCityObject = new(
            sourceFile,
            [cityObject],
            CoordinateReferenceSystem.Parse("EPSG:4326"),
            [CreateTerrainTriangle()],
            TimeSpan.FromSeconds(1.0));
        ParsedSourceFileResult parsedWithoutCityObjects = new(
            sourceFile with { RelativePath = "udx/dem/53394526/empty.gml" },
            [],
            CoordinateReferenceSystem.Parse("EPSG:4326"),
            [CreateTerrainTriangle(), CreateTerrainTriangle()],
            TimeSpan.FromSeconds(2.0));

        DemBootstrapAggregation result = LocalCityGmlDemBootstrapSupport.AggregateDemParsedSourceFiles(
            [parsedWithCityObject, parsedWithoutCityObjects]);

        CachedSourceFileDescriptor cachedSourceFile = Assert.Single(result.CachedDemSourceFiles);
        Assert.Equal("udx/dem/53394525/sample.gml", cachedSourceFile.RelativePath);
        Assert.Single(cachedSourceFile.CityObjects);
        Assert.Equal(3, result.TerrainTriangles.Length);
        Assert.Equal(1, result.ParsedCityObjectCount);
    }

    [Fact]
    public void CreateTerrainHeightTrianglesFanTriangulatesSurfaceVertices()
    {
        BootstrapParsedCityObject cityObject = CreateCityObject();

        TerrainHeightTriangle[] result = LocalCityGmlDemBootstrapSupport.CreateTerrainHeightTriangles([cityObject]);

        Assert.Equal(2, result.Length);
        Assert.Equal(new GeodeticPoint(35.0, 139.0, 10.0), result[0].Vertex0);
        Assert.Equal(new GeodeticPoint(35.0, 139.003, 10.3), result[1].Vertex2);
    }

    [Fact]
    public void CreateDemTerrainTextureOverlaysReturnsDemTextureMetadata()
    {
        DemTerrainBounds demBounds = new(
            SouthLatitude: 35.0,
            NorthLatitude: 35.0001,
            WestLongitude: 139.0,
            EastLongitude: 139.0001);

        TerrainTextureOverlay[] result = LocalCityGmlDemBootstrapSupport.CreateDemTerrainTextureOverlays(
            demBounds,
            ["53394525"]);

        TerrainTextureOverlay overlay = Assert.Single(result);
        Assert.Equal("dem", overlay.PackageName);
        Assert.Equal(LocalCityGmlObjectProjection.DefaultDemTerrainTextureUrlTemplate, overlay.UrlTemplate);
        Assert.Equal(LocalCityGmlObjectProjection.DefaultDemTerrainTextureFallbackUrlTemplate, overlay.FallbackUrlTemplate);
        Assert.Equal(LocalCityGmlObjectProjection.DefaultDemTerrainTextureZoomLevel, overlay.ZoomLevel);
        Assert.Equal(TerrainTextureLicenseMode.PlateauOrthoWithGsiFallback, overlay.LicenseMode);
    }

    [Fact]
    public void CreateDemTerrainTextureOverlaysDeduplicatesExpandedThirdMeshCodes()
    {
        DemTerrainBounds demBounds = new(
            SouthLatitude: 36.225,
            NorthLatitude: 36.2333334,
            WestLongitude: 137.9666666,
            EastLongitude: 137.9791667);

        TerrainTextureOverlay[] result = LocalCityGmlDemBootstrapSupport.CreateDemTerrainTextureOverlays(
            demBounds,
            ["543727", "54372778"]);

        Assert.Equal(4, result.Length);
        Assert.Equal(
            result.Length,
            result.Select(static overlay => overlay.GeographicBounds).Distinct().Count());
    }

    private static BootstrapParsedCityObject CreateCityObject()
    {
        GeodeticPoint[] vertices =
        [
            new(35.0, 139.0, 10.0),
            new(35.0, 139.001, 10.1),
            new(35.0, 139.002, 10.2),
            new(35.0, 139.003, 10.3),
        ];

        return new BootstrapParsedCityObject(
            SlotKey: "dem-sample",
            DisplayName: "dem-sample",
            PackageName: "dem",
            ActualMeshCode: "53394525",
            LodLevel: null,
            Surfaces:
            [
                new BootstrapParsedSurface(
                    PolygonId: "surface",
                    Semantic: BootstrapParsedSurfaceSemantic.Ground,
                    ExteriorRing: new BootstrapParsedRing("ring", vertices, null),
                    InteriorRings: [],
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    TexturePayload: null,
                    UsesGeneratedDemTexture: false),
            ],
            ReferenceSystem: CoordinateReferenceSystem.Parse("EPSG:4326"),
            SourceFileRelativePath: "udx/dem/53394525/sample.gml",
            SourceUnitIdentity: "udx/dem/53394525/sample.gml",
            SourceIdentity: "source",
            SharedAcrossMeshCodes: false,
            TerrainAligned: false,
            OriginOverride: null);
    }

    private static TerrainHeightTriangle CreateTerrainTriangle()
    {
        return new TerrainHeightTriangle(
            new GeodeticPoint(35.0, 139.0, 10.0),
            new GeodeticPoint(35.0, 139.001, 10.1),
            new GeodeticPoint(35.0, 139.002, 10.2));
    }
}
