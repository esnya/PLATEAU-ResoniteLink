using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Application;

public sealed class LocalCityGmlDocumentSetTests
{
    [Fact]
    public void ConstructorStoresPureResultBoundary()
    {
        IPlateauDatasetContentSource datasetSource = new EmptyDatasetContentSource();

        LocalCityGmlDocumentSet documentSet = new(
            datasetSource,
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"],
            ["bldg"],
            [],
            ["53394525"]);

        Assert.Same(datasetSource, documentSet.DatasetSource);
        Assert.Equal(["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"], documentSet.RelativeSourceFiles);
        Assert.Equal(["bldg"], documentSet.PackageNames);
        Assert.Empty(documentSet.TerrainTextureOverlays);
        Assert.Equal(["53394525"], documentSet.RequestedMeshCodes);
    }

    [Fact]
    public void DocumentReadResultSeparatesBootstrapContextFromPureDocumentSet()
    {
        LocalCityGmlDocumentSet documentSet = new(
            new EmptyDatasetContentSource(),
            [],
            [],
            [],
            []);
        SourceFilePipeline[] sourceFilePipelines = [];
        GeodeticPoint globalOriginPoint = new(35.0, 139.0, 0.0);
        LocalCityGmlBootstrapContext bootstrapContext = new(sourceFilePipelines, globalOriginPoint);

        LocalCityGmlDocumentReadResult readResult = new(documentSet, bootstrapContext);

        Assert.Same(documentSet, readResult.DocumentSet);
        Assert.Same(bootstrapContext, readResult.BootstrapContext);
    }

    private sealed class EmptyDatasetContentSource : IPlateauDatasetContentSource
    {
        public string SourcePath => "/tmp/plateau";

        public IReadOnlyList<string> EnumerateFiles()
        {
            return [];
        }

        public bool FileExists(string relativePath)
        {
            return false;
        }

        public ValueTask<Stream> OpenReadAsync(
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            throw new FileNotFoundException(relativePath);
        }

        public Task<string> EnsureLocalFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            throw new FileNotFoundException(relativePath);
        }
    }
}
