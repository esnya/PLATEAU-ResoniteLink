using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class LocalCityGmlDocumentReaderTests
{
    [Fact]
    public async Task ReadAsyncCreatesDocumentSetBoundaryFromStableLocalFixture()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        LocalCityGmlDocumentReader reader = new();

        LocalCityGmlDocumentSet documentSet = await reader.ReadAsync(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                PackageNames: ["bldg"],
                ServerUri: null));

        Assert.Equal(fixturePath, documentSet.DatasetSource.SourcePath);
        Assert.Equal(
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"],
            documentSet.RelativeSourceFiles);
        Assert.Equal(["bldg"], documentSet.PackageNames);
        Assert.Empty(documentSet.TerrainTextureOverlays);
        Assert.Equal(["53394525"], documentSet.RequestedMeshCodes);
    }
}
