using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class LocalCityGmlBootstrapParityTests
{
    [Fact]
    public async Task ReadAsyncMatchesLegacyDocumentSetForMixedObjectFixture()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: TestData.GetFixturePath("LocalPlateauDatasetMixedObjects"),
            PackageNames: ["bldg", "dem", "luse", "tran"],
            ServerUri: null);

        LocalCityGmlDocumentSet pipelineDocumentSet = await LocalCityGmlBootstrapPipeline.ReadAsync(request);
        LocalCityGmlDocumentSet legacyDocumentSet = await LocalCityGmlResonitePlanBuilder.ReadDocumentSetAsync(request);

        AssertDocumentSetParity(pipelineDocumentSet, legacyDocumentSet);
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
        Assert.Equal(expected.BootstrapReferenceSystem.SrsName, actual.BootstrapReferenceSystem.SrsName);
        Assert.Equal(expected.BootstrapReferenceSystem.CompatibilityKey, actual.BootstrapReferenceSystem.CompatibilityKey);
        Assert.Equal(expected.BootstrapReferenceSystem.IsGeographic, actual.BootstrapReferenceSystem.IsGeographic);
        Assert.Equal(expected.BootstrapGlobalOriginPoint, actual.BootstrapGlobalOriginPoint);
        Assert.Equal(expected.BootstrapTerrainHeightSampler is null, actual.BootstrapTerrainHeightSampler is null);

        Assert.Equal(
            expected.BootstrapCachedDemSourceFiles.Select(static descriptor => (descriptor.RelativePath, descriptor.CityObjects.Length)),
            actual.BootstrapCachedDemSourceFiles.Select(static descriptor => (descriptor.RelativePath, descriptor.CityObjects.Length)));

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
}
