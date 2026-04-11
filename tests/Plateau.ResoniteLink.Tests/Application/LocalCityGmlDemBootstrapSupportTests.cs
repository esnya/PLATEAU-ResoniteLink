using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class LocalCityGmlDemBootstrapSupportTests
{
    [Fact]
    public void AggregateDemParsedSourceFilesCombinesCachedFilesAndTriangles()
    {
        LocalCityGmlResonitePlanBuilder.SourceFileDescriptor sourceFile = new(
            "udx/dem/53394525/sample.gml",
            "dem",
            "53394525",
            RequiresMeshAreaFilter: false);

        LocalCityGmlResonitePlanBuilder.ParsedCityObject cityObject = CreateCityObject();
        LocalCityGmlResonitePlanBuilder.ParsedSourceFileResult parsedWithCityObject = new(
            sourceFile,
            [cityObject],
            LocalCityGmlResonitePlanBuilder.CoordinateReferenceSystem.Parse("EPSG:4326"),
            [CreateTerrainTriangle()],
            TimeSpan.FromSeconds(1.0));
        LocalCityGmlResonitePlanBuilder.ParsedSourceFileResult parsedWithoutCityObjects = new(
            sourceFile with { RelativePath = "udx/dem/53394526/empty.gml" },
            [],
            LocalCityGmlResonitePlanBuilder.CoordinateReferenceSystem.Parse("EPSG:4326"),
            [CreateTerrainTriangle(), CreateTerrainTriangle()],
            TimeSpan.FromSeconds(2.0));

        DemBootstrapAggregation result = LocalCityGmlDemBootstrapSupport.AggregateDemParsedSourceFiles(
            [parsedWithCityObject, parsedWithoutCityObjects]);

        LocalCityGmlResonitePlanBuilder.CachedSourceFileDescriptor cachedSourceFile =
            Assert.Single(result.CachedDemSourceFiles);
        Assert.Equal("udx/dem/53394525/sample.gml", cachedSourceFile.RelativePath);
        Assert.Single(cachedSourceFile.CityObjects);
        Assert.Equal(3, result.TerrainTriangles.Length);
        Assert.Equal(1, result.ParsedCityObjectCount);
    }

    [Fact]
    public void CreateTerrainHeightTrianglesFanTriangulatesSurfaceVertices()
    {
        LocalCityGmlResonitePlanBuilder.ParsedCityObject cityObject = CreateCityObject();

        LocalCityGmlResonitePlanBuilder.TerrainHeightTriangle[] result = LocalCityGmlDemBootstrapSupport.CreateTerrainHeightTriangles([cityObject]);

        Assert.Equal(2, result.Length);
        Assert.Equal(new LocalCityGmlResonitePlanBuilder.GeodeticPoint(35.0, 139.0, 10.0), result[0].Vertex0);
        Assert.Equal(new LocalCityGmlResonitePlanBuilder.GeodeticPoint(35.0, 139.003, 10.3), result[1].Vertex2);
    }

    [Fact]
    public void CreateDemTerrainTextureOverlaysReturnsDemTextureMetadata()
    {
        LocalCityGmlResonitePlanBuilder.MeshCodeArea demBounds = new(
            SouthLatitude: 35.0,
            NorthLatitude: 35.0001,
            WestLongitude: 139.0,
            EastLongitude: 139.0001);

        TerrainTextureOverlay[] result = LocalCityGmlDemBootstrapSupport.CreateDemTerrainTextureOverlays(demBounds);

        Assert.Single(result);
        Assert.Equal("dem", result[0].PackageName);
        Assert.StartsWith(LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTexturePath, result[0].TexturePath);
    }

    private static LocalCityGmlResonitePlanBuilder.ParsedCityObject CreateCityObject()
    {
        LocalCityGmlResonitePlanBuilder.GeodeticPoint[] vertices =
        [
            new(35.0, 139.0, 10.0),
            new(35.0, 139.001, 10.1),
            new(35.0, 139.002, 10.2),
            new(35.0, 139.003, 10.3),
        ];

        return new LocalCityGmlResonitePlanBuilder.ParsedCityObject(
            SlotKey: "dem-sample",
            DisplayName: "dem-sample",
            PackageName: "dem",
            ActualMeshCode: "53394525",
            LodLevel: null,
            Surfaces:
            [
                new LocalCityGmlResonitePlanBuilder.ParsedSurface(
                    PolygonId: "surface",
                    Semantic: LocalCityGmlResonitePlanBuilder.ParsedSurfaceSemantic.Ground,
                    ExteriorRing: new LocalCityGmlResonitePlanBuilder.ParsedRing("ring", vertices, null),
                    InteriorRings: [],
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    TexturePath: null),
            ],
            ReferenceSystem: LocalCityGmlResonitePlanBuilder.CoordinateReferenceSystem.Parse("EPSG:4326"),
            SourceIdentity: "source",
            SharedAcrossMeshCodes: false,
            TerrainAligned: false,
            OriginOverride: null);
    }

    private static LocalCityGmlResonitePlanBuilder.TerrainHeightTriangle CreateTerrainTriangle()
    {
        return new LocalCityGmlResonitePlanBuilder.TerrainHeightTriangle(
            new LocalCityGmlResonitePlanBuilder.GeodeticPoint(35.0, 139.0, 10.0),
            new LocalCityGmlResonitePlanBuilder.GeodeticPoint(35.0, 139.001, 10.1),
            new LocalCityGmlResonitePlanBuilder.GeodeticPoint(35.0, 139.002, 10.2));
    }
}
