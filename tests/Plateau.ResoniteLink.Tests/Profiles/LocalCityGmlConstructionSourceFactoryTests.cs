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
        LocalCityGmlConstructionSourceFactory factory = new(reader, composer);
        Action<string> progressReporter = _ => { };

        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: "/tmp/plateau",
            ServerUri: null);

        IImportedSceneSource result = await factory.CreateAsync(request, progressReporter);

        Assert.Same(expectedSource, result);
        Assert.Same(request, reader.LastRequest);
        Assert.Same(progressReporter, reader.LastProgressReporter);
        Assert.Same(request, composer.LastRequest);
        Assert.Same(reader.DocumentSet, composer.LastDocumentSet);
        Assert.Same(progressReporter, composer.LastProgressReporter);
    }

    private sealed class RecordingDocumentReader : ICityGmlDocumentReader
    {
        public PlateauImportRequest? LastRequest { get; private set; }

        public Action<string>? LastProgressReporter { get; private set; }

        public LocalCityGmlDocumentSet DocumentSet { get; } = new(
            new EmptyDatasetContentSource(),
            [],
            [],
            [],
            [],
            [],
            [],
            CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697"),
            new GeodeticPoint(35.0, 139.0, 0.0),
            terrainHeightSampler: null);

        public Task<LocalCityGmlDocumentSet> ReadAsync(
            PlateauImportRequest request,
            Action<string>? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            LastProgressReporter = progressReporter;
            return Task.FromResult(DocumentSet);
        }
    }

    private sealed class RecordingComposer(IImportedSceneSource source) : IImportedSceneSourceComposer
    {
        private readonly IImportedSceneSource source = source;

        public PlateauImportRequest? LastRequest { get; private set; }

        public LocalCityGmlDocumentSet? LastDocumentSet { get; private set; }

        public Action<string>? LastProgressReporter { get; private set; }

        public IImportedSceneSource Compose(
            PlateauImportRequest request,
            LocalCityGmlDocumentSet documentSet,
            Action<string>? progressReporter = null)
        {
            LastRequest = request;
            LastDocumentSet = documentSet;
            LastProgressReporter = progressReporter;
            return source;
        }
    }

    private sealed class StubConstructionSource : IImportedSceneSource
    {
        public ImportedSceneMetadata Metadata { get; } = new(
            SchemaVersion: "3.0",
            SceneName: "stub",
            Request: new PlateauImportRequest(
                Dataset: "stub",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: "/tmp/plateau",
                ServerUri: null),
            SourceDataset: new PlateauSourceDataset([], [], [], []),
            Attribution: new Attribution(
                new LicenseMetadata(
                    RequireCredit: true,
                    CreditText: "credit",
                    LicenseName: "license",
                    LicenseUrl: "https://example.invalid"),
                []),
            GeodeticOrigin: new GeodeticOrigin(35.0, 139.0, 0.0));

        public async IAsyncEnumerable<MaterialBinding> ReadCommonMaterialsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public IEnumerable<ImportedCityObject> ReadCityObjects()
        {
            return [];
        }

        public async IAsyncEnumerable<ImportedCityObject> ReadCityObjectsAsync(
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

        public Task<string> EnsureLocalFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            throw new FileNotFoundException(relativePath);
        }
    }
}
