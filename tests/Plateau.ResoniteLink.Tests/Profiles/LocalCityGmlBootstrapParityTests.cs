using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class LocalCityGmlBootstrapParityTests
{
    [Fact]
    public async Task ReadAsyncMatchesDocumentReaderForMixedObjectFixture()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: TestData.GetFixturePath("LocalPlateauDatasetMixedObjects"),
            PackageNames: ["bldg", "dem", "luse", "tran"],
            ServerUri: null);

        LocalCityGmlDocumentSet pipelineDocumentSet = await LocalCityGmlBootstrapPipeline.ReadAsync(request);
        LocalCityGmlDocumentSet readerDocumentSet = await new LocalCityGmlDocumentReader().ReadAsync(request);
        LocalCityGmlDocumentSet compatibilityDocumentSet = await LocalCityGmlResonitePlanBuilder.ReadDocumentSetAsync(request);

        AssertDocumentSetParity(pipelineDocumentSet, readerDocumentSet);
        AssertDocumentSetParity(pipelineDocumentSet, compatibilityDocumentSet);
        AssertBootstrapSnapshot(
            pipelineDocumentSet,
            expectedRelativeSourceFiles: [
                "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
                "udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml",
                "udx/luse/53394525/plateau_tokyo23ku_luse_53394525.gml",
                "udx/tran/53394525/plateau_tokyo23ku_tran_53394525.gml"],
            expectedPackageNames: ["bldg", "dem", "luse", "tran"],
            expectedRequestedMeshCodes: ["53394525"],
            expectedTerrainSamplerPresent: false);
    }

    [Fact]
    public async Task ReadAsyncMatchesDocumentReaderForParentMeshPackageFixture()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: TestData.GetFixturePath("LocalPlateauDatasetParentMeshPackages"),
            PackageNames: ["bldg", "dem", "luse", "tran"],
            ServerUri: null);

        LocalCityGmlDocumentSet pipelineDocumentSet = await LocalCityGmlBootstrapPipeline.ReadAsync(request);
        LocalCityGmlDocumentSet readerDocumentSet = await new LocalCityGmlDocumentReader().ReadAsync(request);

        AssertDocumentSetParity(pipelineDocumentSet, readerDocumentSet);
        AssertBootstrapSnapshot(
            pipelineDocumentSet,
            expectedRelativeSourceFiles: [
                "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
                "udx/dem/533945/plateau_tokyo23ku_dem_533945.gml",
                "udx/luse/533945/plateau_tokyo23ku_luse_533945.gml",
                "udx/tran/533945/plateau_tokyo23ku_tran_533945.gml"],
            expectedPackageNames: ["bldg", "dem", "luse", "tran"],
            expectedRequestedMeshCodes: ["53394525"],
            expectedTerrainSamplerPresent: false);
    }

    private static void AssertDocumentSetParity(
        LocalCityGmlDocumentSet expected,
        LocalCityGmlDocumentSet actual)
    {
        Assert.Equal(expected.DatasetSource.SourcePath, actual.DatasetSource.SourcePath);
        Assert.Equal(expected.RelativeSourceFiles, actual.RelativeSourceFiles);
        Assert.Equal(expected.PackageNames, actual.PackageNames);
        Assert.Equal(expected.TerrainTextureOverlays, actual.TerrainTextureOverlays);
        Assert.Equal(expected.RequestedMeshCodes, actual.RequestedMeshCodes);
        Assert.Equal(expected.BootstrapReferenceSystem?.SrsName, actual.BootstrapReferenceSystem?.SrsName);
        Assert.Equal(expected.BootstrapReferenceSystem?.CompatibilityKey, actual.BootstrapReferenceSystem?.CompatibilityKey);
        Assert.Equal(expected.BootstrapReferenceSystem?.IsGeographic, actual.BootstrapReferenceSystem?.IsGeographic);
        Assert.Equal(expected.BootstrapGlobalOriginPoint, actual.BootstrapGlobalOriginPoint);
        Assert.Equal(expected.BootstrapTerrainHeightSampler is null, actual.BootstrapTerrainHeightSampler is null);

        Assert.Empty(expected.BootstrapCachedDemSourceFiles);
        Assert.Empty(actual.BootstrapCachedDemSourceFiles);

        SourceFilePipeline[] expectedPipelines = expected.BootstrapSourceFilePipelines.ToArray();
        SourceFilePipeline[] actualPipelines = actual.BootstrapSourceFilePipelines.ToArray();
        Assert.Equal(expectedPipelines.Length, actualPipelines.Length);

        for (int index = 0; index < expectedPipelines.Length; index++)
        {
            Assert.Equal(expectedPipelines[index].SourceFile, actualPipelines[index].SourceFile);

            ParsedSourceFileResult expectedParsed = expectedPipelines[index].GetParseTask().GetAwaiter().GetResult();
            ParsedSourceFileResult actualParsed = actualPipelines[index].GetParseTask().GetAwaiter().GetResult();

            Assert.Equal(expectedParsed.SourceFile, actualParsed.SourceFile);
            Assert.Equal(expectedParsed.ReferenceSystem?.SrsName, actualParsed.ReferenceSystem?.SrsName);
            Assert.Equal(expectedParsed.ReferenceSystem?.CompatibilityKey, actualParsed.ReferenceSystem?.CompatibilityKey);
            Assert.Equal(expectedParsed.ReferenceSystem?.IsGeographic, actualParsed.ReferenceSystem?.IsGeographic);
            Assert.Equal(expectedParsed.CityObjects.Length, actualParsed.CityObjects.Length);
            Assert.Equal(expectedParsed.TerrainTriangles.Length, actualParsed.TerrainTriangles.Length);
        }
    }

    private static void AssertBootstrapSnapshot(
        LocalCityGmlDocumentSet documentSet,
        IReadOnlyList<string> expectedRelativeSourceFiles,
        IReadOnlyList<string> expectedPackageNames,
        IReadOnlyList<string> expectedRequestedMeshCodes,
        bool expectedTerrainSamplerPresent)
    {
        Assert.Equal(expectedRelativeSourceFiles, documentSet.RelativeSourceFiles);
        Assert.Equal(expectedPackageNames, documentSet.PackageNames);
        Assert.Equal(expectedRequestedMeshCodes, documentSet.RequestedMeshCodes);
        Assert.Null(documentSet.BootstrapReferenceSystem);
        Assert.Equal(expectedTerrainSamplerPresent, documentSet.BootstrapTerrainHeightSampler is not null);
        Assert.Empty(documentSet.TerrainTextureOverlays);
        Assert.Empty(documentSet.BootstrapCachedDemSourceFiles);
    }
}
