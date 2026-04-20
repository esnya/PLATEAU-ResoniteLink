using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Application;

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
        Assert.Same(reader.ReadResult, composer.LastReadResult);
        Assert.Same(progressReporter, composer.LastProgressReporter);
    }

    [Fact]
    public async Task CreateAsyncAddsDemOverlaysDuringConstructionWhenBootstrapReadResultIsDiscoveryOnly()
    {
        RecordingDocumentReader reader = new(
            new LocalCityGmlDocumentReadResult(
                new LocalCityGmlDocumentSet(
                    new EmptyDatasetContentSource(),
                    ["udx/dem/53394525/terrain.gml"],
                    ["dem"],
                    [],
                    ["53394525"]),
                new LocalCityGmlBootstrapContext(
                    [],
                    new GeodeticPoint(35.0, 139.0, 0.0))));
        RecordingComposer composer = new(new StubConstructionSource());
        LocalCityGmlConstructionSourceFactory factory = new(reader, composer);
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: "/tmp/plateau",
            PackageNames: ["dem"],
            ServerUri: null);

        _ = await factory.CreateAsync(request);

        Assert.NotSame(reader.ReadResult, composer.LastReadResult);
        Assert.Empty(reader.ReadResult.DocumentSet.TerrainTextureOverlays);
        TerrainTextureOverlay overlay = Assert.Single(composer.LastReadResult!.DocumentSet.TerrainTextureOverlays);
        Assert.Equal("dem", overlay.PackageName);
    }

    [Fact]
    public async Task CreateAsyncRejectsInvalidExplicitDemTextureSourceWhenRecoveredConstructionOverlayHasNoGeoTiffCoverage()
    {
        RecordingDocumentReader reader = new(
            new LocalCityGmlDocumentReadResult(
                new LocalCityGmlDocumentSet(
                    new EmptyDatasetContentSource(),
                    ["udx/dem/53394525/terrain.gml"],
                    ["dem"],
                    [],
                    ["53394525"]),
                new LocalCityGmlBootstrapContext(
                    [],
                    new GeodeticPoint(35.0, 139.0, 0.0))));
        RecordingComposer composer = new(new StubConstructionSource());
        LocalCityGmlConstructionSourceFactory factory = new(
            reader,
            composer,
            new StubDatasetContentSourceFactory(
                new EmptyDatasetContentSource("C:\\ortho")));
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: PlateauImportSource.Local("/tmp/plateau"),
            PackageNames: ["dem"],
            DemTextureSource: PlateauImportSource.Local("C:\\ortho"));

        PlateauImportValidationException exception = await Assert.ThrowsAsync<PlateauImportValidationException>(
            () => factory.CreateAsync(request));

        Assert.Contains(
            exception.Errors,
            static error => error.Contains("GeoTIFF", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class RecordingDocumentReader : ICityGmlDocumentReader
    {
        public RecordingDocumentReader(LocalCityGmlDocumentReadResult? readResult = null)
        {
            ReadResult = readResult ?? new LocalCityGmlDocumentReadResult(
                new LocalCityGmlDocumentSet(
                    new EmptyDatasetContentSource(),
                    [],
                    [],
                    [],
                    []),
                new LocalCityGmlBootstrapContext(
                    [],
                    new GeodeticPoint(35.0, 139.0, 0.0)));
        }

        public PlateauImportRequest? LastRequest { get; private set; }

        public Action<string>? LastProgressReporter { get; private set; }

        public LocalCityGmlDocumentReadResult ReadResult { get; }

        public Task<LocalCityGmlDocumentReadResult> ReadAsync(
            PlateauImportRequest request,
            Action<string>? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            LastProgressReporter = progressReporter;
            return Task.FromResult(ReadResult);
        }
    }

    private sealed class RecordingComposer(IImportedSceneSource source) : IImportedSceneSourceComposer
    {
        private readonly IImportedSceneSource source = source;

        public PlateauImportRequest? LastRequest { get; private set; }

        public LocalCityGmlDocumentReadResult? LastReadResult { get; private set; }

        public Action<string>? LastProgressReporter { get; private set; }

        public IImportedSceneSource Compose(
            PlateauImportRequest request,
            LocalCityGmlDocumentReadResult readResult,
            Action<string>? progressReporter = null)
        {
            LastRequest = request;
            LastReadResult = readResult;
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
        public EmptyDatasetContentSource(string sourcePath = "/tmp/plateau")
        {
            SourcePath = sourcePath;
        }

        public string SourcePath { get; }

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

    private sealed class StubDatasetContentSourceFactory(IPlateauDatasetContentSource datasetSource) : IPlateauDatasetContentSourceFactory
    {
        public Task<IPlateauDatasetContentSource> CreateAsync(
            string sourcePath,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(datasetSource.SourcePath, sourcePath);
            return Task.FromResult(datasetSource);
        }
    }
}
