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
        LocalCityGmlDocumentSet compatibilityDocumentSet = await LocalCityGmlObjectProjection.ReadDocumentSetAsync(request);

        AssertBootstrapContractEquivalent(pipelineDocumentSet, readerDocumentSet);
        AssertBootstrapContractEquivalent(pipelineDocumentSet, compatibilityDocumentSet);
        AssertBootstrapSnapshot(
            pipelineDocumentSet,
            expectedRelativeSourceFiles: [
                "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
                "udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml",
                "udx/luse/53394525/plateau_tokyo23ku_luse_53394525.gml",
                "udx/tran/53394525/plateau_tokyo23ku_tran_53394525.gml"],
            expectedPackageNames: ["bldg", "dem", "luse", "tran"],
            expectedRequestedMeshCodes: ["53394525"],
            expectedTerrainSamplerPresent: false,
            expectedTerrainTextureOverlaysPresent: true);
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

        AssertBootstrapContractEquivalent(pipelineDocumentSet, readerDocumentSet);
        AssertBootstrapSnapshot(
            pipelineDocumentSet,
            expectedRelativeSourceFiles: [
                "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
                "udx/dem/533945/plateau_tokyo23ku_dem_533945.gml",
                "udx/luse/533945/plateau_tokyo23ku_luse_533945.gml",
                "udx/tran/533945/plateau_tokyo23ku_tran_533945.gml"],
            expectedPackageNames: ["bldg", "dem", "luse", "tran"],
            expectedRequestedMeshCodes: ["53394525"],
            expectedTerrainSamplerPresent: false,
            expectedTerrainTextureOverlaysPresent: true);
    }

    [Fact]
    public async Task ReadAsyncRejectsExplicitOrthoSourceWithoutUsableRaster()
    {
        using TemporaryDirectory emptyOrthoDirectory = new();
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Local(TestData.GetFixturePath("LocalPlateauDatasetMixedObjects")),
            PackageNames: ["dem"],
            DemTextureSource: PlateauImportSource.Local(emptyOrthoDirectory.Path));

        PlateauImportValidationException exception = await Assert.ThrowsAsync<PlateauImportValidationException>(
            () => LocalCityGmlBootstrapPipeline.ReadAsync(request));

        Assert.Contains("did not resolve any usable GeoTIFF raster", exception.Errors.Single(), StringComparison.Ordinal);
    }

    private static void AssertBootstrapContractEquivalent(
        LocalCityGmlDocumentSet expected,
        LocalCityGmlDocumentSet actual)
    {
        Assert.Equal(expected.DatasetSource.SourcePath, actual.DatasetSource.SourcePath);
        Assert.Equal(expected.RelativeSourceFiles, actual.RelativeSourceFiles);
        Assert.Equal(expected.PackageNames, actual.PackageNames);
        Assert.Equal(expected.RequestedMeshCodes, actual.RequestedMeshCodes);
        Assert.Equal(expected.BootstrapTerrainHeightSampler is null, actual.BootstrapTerrainHeightSampler is null);
        Assert.Equal(expected.TerrainTextureOverlays.Count > 0, actual.TerrainTextureOverlays.Count > 0);
    }

    private static void AssertBootstrapSnapshot(
        LocalCityGmlDocumentSet documentSet,
        IReadOnlyList<string> expectedRelativeSourceFiles,
        IReadOnlyList<string> expectedPackageNames,
        IReadOnlyList<string> expectedRequestedMeshCodes,
        bool expectedTerrainSamplerPresent,
        bool expectedTerrainTextureOverlaysPresent)
    {
        Assert.Equal(expectedRelativeSourceFiles, documentSet.RelativeSourceFiles);
        Assert.Equal(expectedPackageNames, documentSet.PackageNames);
        Assert.Equal(expectedRequestedMeshCodes, documentSet.RequestedMeshCodes);
        Assert.Null(documentSet.BootstrapReferenceSystem);
        Assert.Equal(expectedTerrainSamplerPresent, documentSet.BootstrapTerrainHeightSampler is not null);
        if (expectedTerrainTextureOverlaysPresent)
        {
            Assert.NotEmpty(documentSet.TerrainTextureOverlays);
            Assert.All(
                documentSet.TerrainTextureOverlays,
                static overlay =>
                {
                    Assert.Equal("dem", overlay.PackageName);
                    Assert.Equal(TerrainTextureLicenseMode.PlateauOrthoOnly, overlay.LicenseMode);
                });
        }
        else
        {
            Assert.Empty(documentSet.TerrainTextureOverlays);
        }
        Assert.Empty(documentSet.BootstrapCachedDemSourceFiles);
    }
}
