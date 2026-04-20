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

    [Fact]
    public async Task CreateAsyncRejectsInvalidExplicitDemTextureSourceWhenRecoveredConstructionOverlayHasNoGeoTiffCoverage()
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

    [Fact]
    public async Task CreateExplicitDemTerrainTextureOverlaysAsyncLimitsCoverageToActualDemGeometry()
    {
        Assert.True(
            PlateauMeshCode.TryGetBounds(
                "53394525",
                out (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) meshBounds));
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        SourceFileDescriptor demSourceFile = new("udx/dem/533945/sample.gml", "dem", "533945", RequiresMeshAreaFilter: false);
        BootstrapParsedCityObject demCityObject = CreateDemParsedCityObject(
            demSourceFile,
            referenceSystem,
            meshBounds.SouthLatitude + 0.0001,
            meshBounds.WestLongitude + 0.0001);
        SourceFilePipeline demPipeline = new(
            demSourceFile,
            () => Task.FromResult(
                new ParsedSourceFileResult(
                    demSourceFile,
                    [demCityObject],
                    referenceSystem,
                    [],
                    TimeSpan.Zero)));
        RecordingDocumentReader reader = new(
            new LocalCityGmlDocumentSet(
                new EmptyDatasetContentSource(),
                [demSourceFile.RelativePath],
                ["dem"],
                [],
                ["533945"],
                [demPipeline],
                [],
                referenceSystem,
                new GeodeticPoint(35.0, 139.0, 0.0),
                terrainHeightSampler: null));
        TerrainTextureOverlay[] overlays = await LocalCityGmlConstructionSourceFactory.CreateExplicitDemTerrainTextureOverlaysAsync(
            reader.DocumentSet,
            [demSourceFile],
            ["533945"],
            demRasterCatalog: null,
            CancellationToken.None);

        TerrainTextureOverlay overlay = Assert.Single(overlays);
        Assert.Empty(overlay.EnumerateGeoReferencedRasterSources());
        Assert.True(
            overlay.GeographicBounds.MinLatitude >= meshBounds.SouthLatitude
            && overlay.GeographicBounds.MaxLatitude <= meshBounds.NorthLatitude
            && overlay.GeographicBounds.MinLongitude >= meshBounds.WestLongitude
            && overlay.GeographicBounds.MaxLongitude <= meshBounds.EastLongitude);
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

        public Task<string> MaterializeFileAsync(
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

    private static BootstrapParsedCityObject CreateDemParsedCityObject(
        SourceFileDescriptor sourceFile,
        CoordinateReferenceSystem referenceSystem,
        double southLatitude,
        double westLongitude)
    {
        GeodeticPoint[] vertices =
        [
            new(southLatitude, westLongitude, 10.0),
            new(southLatitude, westLongitude + 0.001, 10.0),
            new(southLatitude + 0.001, westLongitude + 0.001, 10.0),
            new(southLatitude + 0.001, westLongitude, 10.0),
        ];

        return new BootstrapParsedCityObject(
            SlotKey: "dem",
            DisplayName: "dem",
            PackageName: "dem",
            ActualMeshCode: "53394525",
            LodLevel: 1,
            Surfaces:
            [
                new BootstrapParsedSurface(
                    PolygonId: "surface",
                    Semantic: BootstrapParsedSurfaceSemantic.Ground,
                    ExteriorRing: new BootstrapParsedRing("ring", vertices, null),
                    InteriorRings: [],
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    TexturePayload: null,
                    UsesGeneratedDemTexture: false),
            ],
            ReferenceSystem: referenceSystem,
            SourceFileRelativePath: sourceFile.RelativePath,
            SourceUnitIdentity: sourceFile.RelativePath,
            SourceIdentity: $"{sourceFile.RelativePath}:dem",
            SharedAcrossMeshCodes: false,
            TerrainAligned: false,
            OriginOverride: null);
    }

}
