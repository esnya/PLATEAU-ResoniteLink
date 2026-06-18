using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Application.Importing.Source;

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;


namespace PlateauResoniteLink.Tests.Application;

public sealed class ImportedSceneSourceDatasetTests
{
    [Fact]
    public void ConstructorStoresPureResultBoundary()
    {
        IPlateauDatasetContentSource datasetSource = new EmptyDatasetContentSource();

        ImportedSceneSourceDataset documentSet = new(
            datasetSource,
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"],
            ["bldg"],
            [],
            ["53394525"]);

        Assert.Same(datasetSource, documentSet.DatasetSource);
        Assert.Equal(["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"], documentSet.RelativeSourceFiles);
        Assert.Equal(["bldg"], documentSet.PackageNames);
        Assert.Empty(documentSet.TerrainTextureOverlays);
        Assert.Equal(["53394525"], documentSet.SelectedMeshCodes);
    }

    [Fact]
    public void DiscoverySnapshotSeparatesDiscoveryContextFromPureDocumentSet()
    {
        ImportedSceneSourceDataset documentSet = new(
            new EmptyDatasetContentSource(),
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

        public string? ResolveRelativePath(string baseRelativePath, string candidatePath)
        {
            return null;
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
