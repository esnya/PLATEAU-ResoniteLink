using System;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class DemSourceDiscoverySupportTests
{
    [Fact]
    public void AggregateDemParsedSourceFilesCombinesCachedFilesAndTriangles()
    {
        SourceFileDescriptor sourceFile = new(
            "udx/dem/53394525/sample.gml",
            "dem",
            "53394525",
            RequiresMeshCodeBoundsFilter: false);

        ParsedCityObject cityObject = CreateCityObject();
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

        DemDiscoveryAggregation result = DemSourceDiscoverySupport.AggregateDemParsedSourceFiles(
            [parsedWithCityObject, parsedWithoutCityObjects]);

        CachedSourceFileDescriptor cachedSourceFile = Assert.Single(result.CachedDemSourceFiles);
        Assert.Equal("udx/dem/53394525/sample.gml", cachedSourceFile.RelativePath);
        Assert.Equal(CoordinateReferenceSystem.Parse("EPSG:4326"), cachedSourceFile.ReferenceSystem);
        Assert.Equal(3, result.TerrainTriangles.Length);
        Assert.Equal(1, result.ParsedCityObjectCount);
    }

    [Fact]
    public void CreateDemTerrainOverlayRegionsReturnsRequestedThirdMeshBounds()
    {
        Assert.True(
            PlateauMeshCode.TryGetBounds(
                "53394525",
                out (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) meshBounds));
        DemTerrainOverlayRegion[] result = DemSourceDiscoverySupport.CreateDemTerrainOverlayRegions(
            ["53394525"]);

        DemTerrainOverlayRegion region = Assert.Single(result);
        Assert.Equal(ThirdRegionalMeshCode.Parse("53394525"), region.MeshCode);
        Assert.Equal(meshBounds.SouthLatitude, region.GeographicBounds.MinLatitude);
        Assert.Equal(meshBounds.NorthLatitude, region.GeographicBounds.MaxLatitude);
        Assert.Equal(meshBounds.WestLongitude, region.GeographicBounds.MinLongitude);
        Assert.Equal(meshBounds.EastLongitude, region.GeographicBounds.MaxLongitude);
    }

    [Fact]
    public void CreateDemTerrainOverlayRegionsReturnsEmptyWhenRequestedMeshesDoNotIntersect()
    {
        DemTerrainOverlayRegion[] regions = DemSourceDiscoverySupport.CreateDemTerrainOverlayRegions(
                new DemTerrainBounds(35.0, 35.0001, 139.0, 139.0001),
                ["99999999"]);

        Assert.Empty(regions);
    }

    [Fact]
    public void CreateDemTerrainOverlayRegionsExpandsSecondMeshToThirdMeshRegions()
    {
        DemTerrainOverlayRegion[] regions = DemSourceDiscoverySupport.CreateDemTerrainOverlayRegions(["533945"]);

        Assert.Equal(100, regions.Length);
        Assert.All(regions, static region => Assert.Equal("533945", region.MeshCode.Parent.Value));
    }

    [Fact]
    public void ResolveDemTerrainBoundsFallsBackWhenParsedDemObjectsHaveNoVertices()
    {
        SourceFileDescriptor sourceFile = new(
            "udx/dem/53394525/empty-geometry.gml",
            "dem",
            "53394525",
            RequiresMeshCodeBoundsFilter: false);
        ParsedSourceFileResult parsedWithoutVertices = new(
            sourceFile,
            [CreateCityObjectWithoutVertices()],
            CoordinateReferenceSystem.Parse("EPSG:4326"),
            [],
            TimeSpan.Zero);
        DemTerrainBounds fallbackBounds = new(35.0, 35.01, 139.0, 139.01);

        DemTerrainBounds? result = DemSourceDiscoverySupport.ResolveDemTerrainBounds(
            [parsedWithoutVertices],
            fallbackBounds);

        Assert.Equal(fallbackBounds, result);
    }

    private static ParsedCityObject CreateCityObject()
    {
        GeodeticPoint[] vertices =
        [
            new(35.0, 139.0, 10.0),
            new(35.0, 139.001, 10.1),
            new(35.0, 139.002, 10.2),
            new(35.0, 139.003, 10.3),
        ];

        return new ParsedCityObject(
            SlotKey: "dem-sample",
            DisplayName: "dem-sample",
            PackageName: "dem",
            ActualMeshCode: "53394525",
            LodLevel: null,
            Surfaces:
            [
                new ParsedSurface(
                    Semantic: ParsedSurfaceSemantic.Ground,
                    ExteriorRing: new ParsedRing(vertices, null),
                    InteriorRings: [],
                    BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
                    TexturePayload: null,
                    UsesGeneratedDemTexture: false),
            ],
            ReferenceSystem: CoordinateReferenceSystem.Parse("EPSG:4326"),
            SourceFileRelativePath: "udx/dem/53394525/sample.gml",
            SharedAcrossMeshCodes: false,
            BuildingAttributes: BuildingAttributeContext.Empty,
            TerrainAligned: false,
            GeodeticOriginOverride: null);
    }

    private static ParsedCityObject CreateCityObjectWithoutVertices()
    {
        return new ParsedCityObject(
            SlotKey: "dem-empty",
            DisplayName: "dem-empty",
            PackageName: "dem",
            ActualMeshCode: "53394525",
            LodLevel: null,
            Surfaces:
            [
                new ParsedSurface(
                    Semantic: ParsedSurfaceSemantic.Ground,
                    ExteriorRing: new ParsedRing([], null),
                    InteriorRings: [],
                    BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
                    TexturePayload: null,
                    UsesGeneratedDemTexture: false),
            ],
            ReferenceSystem: CoordinateReferenceSystem.Parse("EPSG:4326"),
            SourceFileRelativePath: "udx/dem/53394525/empty-geometry.gml",
            SharedAcrossMeshCodes: false,
            BuildingAttributes: BuildingAttributeContext.Empty,
            TerrainAligned: false,
            GeodeticOriginOverride: null);
    }

    private static TerrainHeightTriangle CreateTerrainTriangle()
    {
        return new TerrainHeightTriangle(
            new GeodeticPoint(35.0, 139.0, 10.0),
            new GeodeticPoint(35.0, 139.001, 10.1),
            new GeodeticPoint(35.0, 139.002, 10.2));
    }
}
