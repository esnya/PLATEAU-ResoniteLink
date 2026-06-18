using PlateauResoniteLink.Plateau.Application.Importing;
using PlateauResoniteLink.Plateau.Application.Importing.Source;

namespace PlateauResoniteLink.Tests.Application;

public sealed class ImportedSceneSourceDatasetTests
{
    [Fact]
    public void ConstructorStoresPureResultBoundary()
    {
        ImportedSceneSourceDataset documentSet = new(
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"],
            ["bldg"],
            [],
            ["53394525"]);

        Assert.Equal(["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"], documentSet.RelativeSourceFiles);
        Assert.Equal(["bldg"], documentSet.PackageNames);
        Assert.Empty(documentSet.TerrainTextureOverlays);
        Assert.Equal(["53394525"], documentSet.SelectedMeshCodes);
    }

    [Fact]
    public void DiscoverySnapshotSeparatesDiscoveryContextFromPureDocumentSet()
    {
        ImportedSceneSourceDataset documentSet = new(
            [],
            [],
            [],
            []);
        SourceFilePipeline[] sourceFilePipelines = [];
        GeodeticPoint globalOriginPoint = new(35.0, 139.0, 0.0);
        ImportedSceneSourceContext discoveryContext = new(sourceFilePipelines, globalOriginPoint);

        ImportedSceneSourceSnapshot readResult = new(documentSet, discoveryContext);

        Assert.Same(documentSet, readResult.DocumentSet);
        Assert.Same(discoveryContext, readResult.DiscoveryContext);
    }
}
