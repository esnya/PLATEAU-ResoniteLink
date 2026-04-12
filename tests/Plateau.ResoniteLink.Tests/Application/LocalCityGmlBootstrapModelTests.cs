using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class LocalCityGmlBootstrapModelTests
{
    [Fact]
    public async Task SourceFilePipelineCachesParseTaskFactoryResult()
    {
        int factoryCallCount = 0;
        ParsedSourceFileResult expected = CreateParsedSourceFileResult();
        SourceFilePipeline pipeline = new(
            expected.SourceFile,
            () =>
            {
                factoryCallCount++;
                return Task.FromResult(expected);
            });

        ParsedSourceFileResult first = await pipeline.GetParseTask();
        ParsedSourceFileResult second = await pipeline.GetParseTask();

        Assert.Equal(1, factoryCallCount);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task SourceFilePipelineToLegacyPreservesSingleEvaluation()
    {
        int factoryCallCount = 0;
        ParsedSourceFileResult expected = CreateParsedSourceFileResult();
        SourceFilePipeline pipeline = new(
            expected.SourceFile,
            () =>
            {
                factoryCallCount++;
                return Task.FromResult(expected);
            });

        LocalCityGmlResonitePlanBuilder.SourceFilePipeline legacy = pipeline.ToLegacy();
        LocalCityGmlResonitePlanBuilder.ParsedSourceFileResult first = await legacy.GetParseTask();
        LocalCityGmlResonitePlanBuilder.ParsedSourceFileResult second = await legacy.GetParseTask();

        Assert.Equal(1, factoryCallCount);
        Assert.Same(first, second);
        Assert.Equal(expected.SourceFile.RelativePath, first.SourceFile.RelativePath);
    }

    [Fact]
    public void ParsedSourceFileResultRoundTripsThroughLegacy()
    {
        ParsedSourceFileResult expected = CreateParsedSourceFileResult();

        ParsedSourceFileResult actual = ParsedSourceFileResult.FromLegacy(expected.ToLegacy());

        Assert.Equal(expected.SourceFile, actual.SourceFile);
        Assert.NotNull(actual.ReferenceSystem);
        Assert.Equal(expected.ReferenceSystem?.SrsName, actual.ReferenceSystem.SrsName);
        Assert.Equal(expected.ReferenceSystem?.CompatibilityKey, actual.ReferenceSystem.CompatibilityKey);
        Assert.Equal(expected.ReferenceSystem?.IsGeographic, actual.ReferenceSystem.IsGeographic);
        Assert.Equal(expected.TerrainTriangles, actual.TerrainTriangles);
        Assert.Equal(expected.Elapsed, actual.Elapsed);
        BootstrapParsedCityObject actualCityObject = Assert.Single(actual.CityObjects);
        BootstrapParsedCityObject expectedCityObject = Assert.Single(expected.CityObjects);
        Assert.Equal(expectedCityObject.SlotKey, actualCityObject.SlotKey);
        Assert.Equal(expectedCityObject.DisplayName, actualCityObject.DisplayName);
        Assert.Equal(expectedCityObject.PackageName, actualCityObject.PackageName);
        Assert.Equal(expectedCityObject.ActualMeshCode, actualCityObject.ActualMeshCode);
        Assert.Equal(expectedCityObject.LodLevel, actualCityObject.LodLevel);
        Assert.Equal(expectedCityObject.SourceIdentity, actualCityObject.SourceIdentity);
        Assert.Equal(expectedCityObject.SharedAcrossMeshCodes, actualCityObject.SharedAcrossMeshCodes);
        Assert.Equal(expectedCityObject.TerrainAligned, actualCityObject.TerrainAligned);
        BootstrapParsedSurface actualSurface = Assert.Single(actualCityObject.Surfaces);
        BootstrapParsedSurface expectedSurface = Assert.Single(expectedCityObject.Surfaces);
        Assert.Equal(expectedSurface.PolygonId, actualSurface.PolygonId);
        Assert.Equal(expectedSurface.Semantic, actualSurface.Semantic);
        Assert.Equal(expectedSurface.BaseColor, actualSurface.BaseColor);
        Assert.Equal(expectedSurface.TexturePath, actualSurface.TexturePath);
        Assert.Equal(expectedSurface.ExteriorRing.Vertices, actualSurface.ExteriorRing.Vertices);
    }

    [Fact]
    public void DemTerrainBoundsRoundTripsThroughLegacy()
    {
        DemTerrainBounds expected = new(35.0, 35.1, 139.0, 139.1);

        DemTerrainBounds actual = DemTerrainBounds.FromLegacy(expected.ToLegacy());

        Assert.Equal(expected, actual);
    }

    private static ParsedSourceFileResult CreateParsedSourceFileResult()
    {
        SourceFileDescriptor sourceFile = new(
            "udx/bldg/53394525/sample.gml",
            "bldg",
            "53394525",
            RequiresMeshAreaFilter: false);

        BootstrapParsedCityObject cityObject = new(
            SlotKey: "building-1",
            DisplayName: "building-1",
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 1,
            Surfaces:
            [
                new BootstrapParsedSurface(
                    PolygonId: "surface-1",
                    Semantic: BootstrapParsedSurfaceSemantic.Wall,
                    ExteriorRing: new BootstrapParsedRing(
                        "ring-1",
                        [
                            new GeodeticPoint(35.0, 139.0, 10.0),
                            new GeodeticPoint(35.0, 139.001, 10.0),
                            new GeodeticPoint(35.001, 139.001, 10.5),
                        ],
                        null),
                    InteriorRings: [],
                    BaseColor: new ResoniteColor(0.1, 0.2, 0.3, 1.0),
                    TexturePath: "textures/wall.png"),
            ],
            ReferenceSystem: CoordinateReferenceSystem.Parse("EPSG:4326"),
            SourceIdentity: "bldg:sample",
            SharedAcrossMeshCodes: false);

        return new ParsedSourceFileResult(
            sourceFile,
            [cityObject],
            CoordinateReferenceSystem.Parse("EPSG:4326"),
            [
                new TerrainHeightTriangle(
                    new GeodeticPoint(35.0, 139.0, 10.0),
                    new GeodeticPoint(35.0, 139.001, 10.1),
                    new GeodeticPoint(35.001, 139.0, 10.2)),
            ],
            TimeSpan.FromMilliseconds(250));
    }
}
