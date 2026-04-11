using Plateau.ResoniteLink.Application.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class LocalCityGmlDocumentSetTests
{
    [Fact]
    public void ConstructorStoresBootstrapStateVerbatim()
    {
        IPlateauDatasetContentSource datasetSource = new EmptyDatasetContentSource();
        LocalCityGmlResonitePlanBuilder.SourceFilePipeline[] sourceFilePipelines = [];
        IReadOnlyList<LocalCityGmlResonitePlanBuilder.CachedSourceFileDescriptor> cachedDemSourceFiles = [];
        LocalCityGmlResonitePlanBuilder.CoordinateReferenceSystem referenceSystem =
            LocalCityGmlResonitePlanBuilder.CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        LocalCityGmlResonitePlanBuilder.GeodeticPoint globalOriginPoint = new(35.0, 139.0, 0.0);

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
        Assert.Same(sourceFilePipelines, documentSet.SourceFilePipelines);
        Assert.Same(cachedDemSourceFiles, documentSet.CachedDemSourceFiles);
        Assert.Equal(referenceSystem, documentSet.ReferenceSystem);
        Assert.Equal(globalOriginPoint, documentSet.GlobalOriginPoint);
        Assert.Null(documentSet.TerrainHeightSampler);
        Assert.Equal(["bldg"], documentSet.BootstrapState.PackageNames);
        Assert.Equal("http://www.opengis.net/def/crs/EPSG/0/6697", documentSet.BootstrapState.ReferenceSystem.SrsName);
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

        public Task<string> MaterializeFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            throw new FileNotFoundException(relativePath);
        }
    }
}
