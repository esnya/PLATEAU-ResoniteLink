using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Application;

public sealed class LocalCityGmlDocumentReaderTests
{
    [Fact]
    public async Task ReadAsyncCreatesDocumentSetBoundaryFromStableLocalFixture()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        LocalCityGmlDocumentReader reader = new(
            new DefaultPlateauDatasetContentSourceFactory(
                new RemoteArchiveDistributionPolicy(),
                new ArchiveFileLayoutPolicy()),
            new CityGmlAppearanceStoreFactory(),
            new CityGmlLodSelector());

        LocalCityGmlDocumentReadResult readResult = await reader.ReadAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                PackageNames: ["bldg"],
                ServerUri: null));
        LocalCityGmlDocumentSet documentSet = readResult.DocumentSet;

        Assert.Equal(fixturePath, documentSet.DatasetSource.SourcePath);
        Assert.Equal(
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"],
            documentSet.RelativeSourceFiles);
        Assert.Equal(["bldg"], documentSet.PackageNames);
        Assert.Empty(documentSet.TerrainTextureOverlays);
        Assert.Equal(["53394525"], documentSet.RequestedMeshCodes);
    }
}
