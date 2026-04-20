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

        IResoniteConstructionSource result = await factory.CreateAsync(request, progressReporter);

        Assert.Same(expectedSource, result);
        Assert.Same(request, reader.LastRequest);
        Assert.Same(progressReporter, reader.LastProgressReporter);
        Assert.Same(request, composer.LastRequest);
        Assert.Same(reader.DocumentSet, composer.LastDocumentSet);
        Assert.Same(progressReporter, composer.LastProgressReporter);
    }

    [Fact]
    public async Task CreateAsyncAddsDemOverlaysDuringConstructionWhenBootstrapDocumentSetIsDiscoveryOnly()
    {
        RecordingDocumentReader reader = new(
            new LocalCityGmlDocumentSet(
                new EmptyDatasetContentSource(),
                ["udx/dem/53394525/terrain.gml"],
                ["dem"],
                [],
                ["53394525"],
                [],
                [],
                CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697"),
                new GeodeticPoint(35.0, 139.0, 0.0),
                terrainHeightSampler: null));
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

        Assert.NotSame(reader.DocumentSet, composer.LastDocumentSet);
        Assert.Empty(reader.DocumentSet.TerrainTextureOverlays);
        TerrainTextureOverlay overlay = Assert.Single(composer.LastDocumentSet!.TerrainTextureOverlays);
        Assert.Equal("dem", overlay.PackageName);
    }

    private sealed class RecordingDocumentReader : ICityGmlDocumentReader
    {
        public RecordingDocumentReader(LocalCityGmlDocumentSet? documentSet = null)
        {
            DocumentSet = documentSet ?? new LocalCityGmlDocumentSet(
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
        }

        public PlateauImportRequest? LastRequest { get; private set; }

        public Action<string>? LastProgressReporter { get; private set; }

        public LocalCityGmlDocumentSet DocumentSet { get; }

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

    private sealed class RecordingComposer(IResoniteConstructionSource source) : IResoniteConstructionComposer
    {
        private readonly IResoniteConstructionSource source = source;

        public PlateauImportRequest? LastRequest { get; private set; }

        public LocalCityGmlDocumentSet? LastDocumentSet { get; private set; }

        public Action<string>? LastProgressReporter { get; private set; }

        public IResoniteConstructionSource Compose(
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

        public async IAsyncEnumerable<ResoniteMaterialBinding> ReadCommonMaterialsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

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
