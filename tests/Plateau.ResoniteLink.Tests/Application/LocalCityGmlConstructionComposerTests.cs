using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class LocalCityGmlConstructionComposerTests
{
    [Fact]
    public void ComposeCopiesDocumentSetStateIntoConstructionMetadata()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: "/tmp/plateau",
            ServerUri: null);
        LocalCityGmlDocumentSet documentSet = CreateDocumentSet();
        LocalCityGmlConstructionComposer composer = new(new StubGeometryProjector());

        IResoniteConstructionSource source = composer.Compose(request, documentSet);

        Assert.Equal("3.0", source.Metadata.SchemaVersion);
        Assert.Equal("PLATEAU tokyo23ku 53394525", source.Metadata.WorldName);
        Assert.Same(request, source.Metadata.Request);
        Assert.Equal(documentSet.PackageNames, source.Metadata.SourceDataset.PackageNames);
        Assert.Equal(documentSet.RelativeSourceFiles, source.Metadata.SourceDataset.SourceFiles);
        Assert.Equal(documentSet.TerrainTextureOverlays, source.Metadata.SourceDataset.TerrainTextureOverlays);
        Assert.Equal(documentSet.RequestedMeshCodes, source.Metadata.SourceDataset.RequestedMeshCodes);
        Assert.Equal(documentSet.GlobalOriginPoint.Latitude, source.Metadata.LocalOrigin.Latitude);
        Assert.Equal(documentSet.GlobalOriginPoint.Longitude, source.Metadata.LocalOrigin.Longitude);
        Assert.Equal(documentSet.GlobalOriginPoint.Altitude, source.Metadata.LocalOrigin.Altitude);
    }

    private static LocalCityGmlDocumentSet CreateDocumentSet()
    {
        return new LocalCityGmlDocumentSet(
            new EmptyDatasetContentSource(),
            ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"],
            ["bldg"],
            [],
            ["53394525"],
            [],
            [],
            LocalCityGmlResonitePlanBuilder.CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697"),
            new LocalCityGmlResonitePlanBuilder.GeodeticPoint(35.0, 139.0, 0.0),
            terrainHeightSampler: null);
    }

    private sealed class StubGeometryProjector : ICityGmlGeometryProjector
    {
        public IEnumerable<ResoniteConstructionCityObject> MaterializeCityObjects(
            LocalCityGmlGeometryProjectionContext projectionContext,
            PlateauImportRequest request)
        {
            return [];
        }
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
