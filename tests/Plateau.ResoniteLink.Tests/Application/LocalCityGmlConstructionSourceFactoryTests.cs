using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class LocalCityGmlConstructionSourceFactoryTests
{
    [Fact]
    public async Task CreateAsyncUsesDocumentReaderAndComposer()
    {
        StubConstructionSource expectedSource = new();
        RecordingDocumentReader reader = new();
        RecordingComposer composer = new(expectedSource);
        LocalCityGmlConstructionSourceFactory factory = CreateFactory(reader, composer);

        PlateauImportRequest request = CreateRequest();

        IResoniteConstructionSource result = await factory.CreateAsync(request);

        Assert.Same(expectedSource, result);
        Assert.Same(request, reader.LastRequest);
        Assert.Same(request, composer.LastRequest);
        Assert.Same(reader.DocumentSet, composer.LastDocumentSet);
    }

    private static LocalCityGmlConstructionSourceFactory CreateFactory(
        ICityGmlDocumentReader documentReader,
        IResoniteConstructionComposer constructionComposer)
    {
        return new LocalCityGmlConstructionSourceFactory(documentReader, constructionComposer);
    }

    private static PlateauImportRequest CreateRequest()
    {
        return new PlateauImportRequest(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: "/tmp/plateau",
            ServerUri: null);
    }
    private sealed class RecordingDocumentReader : ICityGmlDocumentReader
    {
        public PlateauImportRequest? LastRequest { get; private set; }

        public LocalCityGmlDocumentSet DocumentSet { get; } = CreateDocumentSet();

        public Task<LocalCityGmlDocumentSet> ReadAsync(
            PlateauImportRequest request,
            Action<string>? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(DocumentSet);
        }
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

    private sealed class RecordingComposer(IResoniteConstructionSource source) : IResoniteConstructionComposer
    {
        private readonly IResoniteConstructionSource source = source;

        public PlateauImportRequest? LastRequest { get; private set; }

        public LocalCityGmlDocumentSet? LastDocumentSet { get; private set; }

        public IResoniteConstructionSource Compose(
            PlateauImportRequest request,
            LocalCityGmlDocumentSet documentSet)
        {
            LastRequest = request;
            LastDocumentSet = documentSet;
            return source;
        }
    }

    private sealed class StubConstructionSource : IResoniteConstructionSource
    {
        public ResoniteConstructionMetadata Metadata { get; } = new(
            SchemaVersion: "3.0",
            WorldName: "stub",
            Request: new PlateauImportRequest(
                Dataset: "stub",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: "/tmp/plateau",
                ServerUri: null),
            SourceDataset: new PlateauSourceDataset([], [], [], []),
            Attribution: new ResoniteAttribution(
                new ResoniteLicenseComponentMetadata(
                    RequireCredit: true,
                    CreditText: "credit",
                    LicenseName: "license",
                    LicenseUrl: "https://example.invalid"),
                []),
            LocalOrigin: new ResoniteLocalOrigin(35.0, 139.0, 0.0));

        public IEnumerable<ResoniteConstructionCityObject> ReadCityObjects()
        {
            return [];
        }

        public async IAsyncEnumerable<ResoniteConstructionCityObject> ReadCityObjectsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
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
