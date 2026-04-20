using Plateau.ResoniteLink.Application.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class LocalCityGmlDocumentSetTests
{
    [Fact]
    public void ConstructorStoresBootstrapStateVerbatim()
    {
        IPlateauDatasetContentSource datasetSource = new EmptyDatasetContentSource();
        SourceFilePipeline[] sourceFilePipelines = [];
        IReadOnlyList<CachedSourceFileDescriptor> cachedDemSourceFiles = [];
        CoordinateReferenceSystem referenceSystem =
            CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        GeodeticPoint globalOriginPoint = new(35.0, 139.0, 0.0);

        LocalCityGmlDocumentSet documentSet = new(
            datasetSource,
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"],
            ["bldg"],
            [],
            ["53394525"],
            sourceFilePipelines,
            cachedDemSourceFiles,
            referenceSystem,
            globalOriginPoint,
            terrainHeightSampler: null);

        Assert.Same(datasetSource, documentSet.DatasetSource);
        Assert.Equal(["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"], documentSet.RelativeSourceFiles);
        Assert.Equal(["bldg"], documentSet.PackageNames);
        Assert.Empty(documentSet.TerrainTextureOverlays);
        Assert.Equal(["53394525"], documentSet.RequestedMeshCodes);
        Assert.Same(sourceFilePipelines, documentSet.BootstrapSourceFilePipelines);
        Assert.Same(cachedDemSourceFiles, documentSet.BootstrapCachedDemSourceFiles);
        Assert.Equal(referenceSystem, documentSet.BootstrapReferenceSystem);
        Assert.Equal(globalOriginPoint, documentSet.BootstrapGlobalOriginPoint);
        Assert.Null(documentSet.BootstrapTerrainHeightSampler);
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
