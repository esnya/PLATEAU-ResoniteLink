using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class LocalCityGmlConstructionSourceFactoryTests
{
    [Fact]
    public async Task CreateAsyncUsesDocumentReaderAndComposer()
    {
        StubConstructionSource expectedSource = new();
        RecordingDocumentReader reader = new();
        RecordingComposer composer = new(expectedSource);
        StubDemTextureSourcePolicy demTextureSourcePolicy = new([]);
        LocalCityGmlConstructionSourceFactory factory = new(reader, composer, demTextureSourcePolicy);
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
        Assert.Null(demTextureSourcePolicy.LastRequest);
    }

    [Fact]
    public async Task CreateAsyncAddsDemOverlaysDuringConstructionWhenBootstrapReadResultIsDiscoveryOnly()
    {
        TerrainTextureOverlay[] resolvedOverlays =
        [
            new(
                PackageName: "dem",
                GeographicBounds: new GeographicRectangle(35.0, 35.01, 139.0, 139.01),
                MaxTextureSize: 1024,
                Sources:
                [
                    new TerrainTextureTileSource("https://tiles.example/{z}/{x}/{y}.png", 18),
                ]),
        ];
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
        StubDemTextureSourcePolicy demTextureSourcePolicy = new(resolvedOverlays);
        LocalCityGmlConstructionSourceFactory factory = new(reader, composer, demTextureSourcePolicy);
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
        Assert.Same(resolvedOverlays[0], overlay);
        Assert.Same(request, demTextureSourcePolicy.LastRequest);
        Assert.Equal(["53394525"], demTextureSourcePolicy.LastOverlayRegionIdentities);
    }

    [Fact]
    public async Task CreateAsyncFiltersPolicyMeshCodesToDemRequestedFiles()
    {
        RecordingDocumentReader reader = new(
            new LocalCityGmlDocumentReadResult(
                new LocalCityGmlDocumentSet(
                    new EmptyDatasetContentSource(),
                    [
                        "udx/dem/53394525/terrain.gml",
                        "udx/bldg/53394526/building.gml",
                    ],
                    ["dem", "bldg"],
                    [],
                    ["53394525", "53394526"]),
                new LocalCityGmlBootstrapContext(
                    [],
                    new GeodeticPoint(35.0, 139.0, 0.0))));
        RecordingComposer composer = new(new StubConstructionSource());
        StubDemTextureSourcePolicy demTextureSourcePolicy = new([]);
        LocalCityGmlConstructionSourceFactory factory = new(reader, composer, demTextureSourcePolicy);
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525|53394526",
            Source: DatasetLocation.Local("/tmp/plateau"),
            PackageNames: ["dem", "bldg"],
            DemTextureSource: DatasetLocation.Local("C:\\ortho"));

        _ = await factory.CreateAsync(request);

        Assert.Equal(["53394525"], demTextureSourcePolicy.LastOverlayRegionIdentities);
    }

    [Fact]
    public async Task CreateAsyncDerivesDemOverlayRegionsFromParsedDemGeometry()
    {
        CoordinateReferenceSystem referenceSystem =
            CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        SourceFileDescriptor demSourceFile = new(
            "udx/dem/53394525/terrain.gml",
            "dem",
            "53394525",
            RequiresMeshAreaFilter: false);
        SourceFilePipeline demPipeline = new(
            demSourceFile,
            () => Task.FromResult(
                new ParsedSourceFileResult(
                    demSourceFile,
                    [CreateParsedDemCityObject(referenceSystem)],
                    referenceSystem,
                    [],
                    TimeSpan.Zero)));
        RecordingDocumentReader reader = new(
            new LocalCityGmlDocumentReadResult(
                new LocalCityGmlDocumentSet(
                    new EmptyDatasetContentSource(),
                    [
                        "udx/dem/53394525/terrain.gml",
                        "udx/bldg/53394526/building.gml",
                    ],
                    ["dem", "bldg"],
                    [],
                    ["53394525", "53394526"]),
                new LocalCityGmlBootstrapContext(
                    [demPipeline],
                    new GeodeticPoint(35.0, 139.0, 0.0))));
        RecordingComposer composer = new(new StubConstructionSource());
        StubDemTextureSourcePolicy demTextureSourcePolicy = new([]);
        LocalCityGmlConstructionSourceFactory factory = new(reader, composer, demTextureSourcePolicy);
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525|53394526",
            Source: DatasetLocation.Local("/tmp/plateau"),
            PackageNames: ["dem", "bldg"],
            DemTextureSource: DatasetLocation.Local("C:\\ortho"));

        _ = await factory.CreateAsync(request);

        Assert.Equal(["53394525"], demTextureSourcePolicy.LastOverlayRegionIdentities);
    }

    [Fact]
    public async Task CreateAsyncPropagatesDemTextureSourceValidationFromPolicy()
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
        StubDemTextureSourcePolicy demTextureSourcePolicy = new(
            [],
            new PlateauImportValidationException(["invalid GeoTIFF source"]));
        LocalCityGmlConstructionSourceFactory factory = new(reader, composer, demTextureSourcePolicy);
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: DatasetLocation.Local("/tmp/plateau"),
            PackageNames: ["dem"],
            DemTextureSource: DatasetLocation.Local("C:\\ortho"));

        PlateauImportValidationException exception = await Assert.ThrowsAsync<PlateauImportValidationException>(
            () => factory.CreateAsync(request));

        Assert.Contains(
            exception.Errors,
            static error => error.Contains("GeoTIFF", StringComparison.OrdinalIgnoreCase));
        Assert.Null(composer.LastReadResult);
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

        public async IAsyncEnumerable<ImportedCityObject> ReadCityObjectsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private static BootstrapParsedCityObject CreateParsedDemCityObject(CoordinateReferenceSystem referenceSystem)
    {
        _ = PlateauMeshCode.TryGetBounds(
            "53394525",
            out (double SouthLatitude, double NorthLatitude, double WestLongitude, double EastLongitude) bounds);
        double centerLatitude = (bounds.SouthLatitude + bounds.NorthLatitude) * 0.5;
        double centerLongitude = (bounds.WestLongitude + bounds.EastLongitude) * 0.5;

        return new BootstrapParsedCityObject(
            SlotKey: "dem-slot-000",
            DisplayName: "dem-slot-000",
            PackageName: "dem",
            ActualMeshCode: "53394525",
            LodLevel: 1,
            Surfaces:
            [
                new BootstrapParsedSurface(
                    PolygonId: "dem-surface-000",
                    Semantic: BootstrapParsedSurfaceSemantic.Ground,
                    ExteriorRing: new BootstrapParsedRing(
                        "dem-ring-000",
                        [
                            new GeodeticPoint(bounds.SouthLatitude, bounds.WestLongitude, 0.0),
                            new GeodeticPoint(bounds.SouthLatitude, centerLongitude, 0.0),
                            new GeodeticPoint(centerLatitude, centerLongitude, 0.0),
                            new GeodeticPoint(centerLatitude, bounds.WestLongitude, 0.0),
                        ],
                        UVs: null),
                    InteriorRings: [],
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    TexturePayload: null,
                    UsesGeneratedDemTexture: true),
            ],
            ReferenceSystem: referenceSystem,
            SourceFileRelativePath: "udx/dem/53394525/terrain.gml",
            SourceUnitIdentity: "test-unit",
            SourceIdentity: "dem:slot-000",
            SharedAcrossMeshCodes: false);
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

    private sealed class StubDemTextureSourcePolicy(
        IReadOnlyList<TerrainTextureOverlay> overlays,
        PlateauImportValidationException? exception = null) : IDemTextureSourcePolicy
    {
        public PlateauImportRequest? LastRequest { get; private set; }

        public IReadOnlyList<string>? LastOverlayRegionIdentities { get; private set; }

        public Task<ResolvedDemTextureSources> ResolveAsync(
            PlateauImportRequest request,
            IReadOnlyList<DemTerrainOverlayRegion> overlayRegions,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            LastOverlayRegionIdentities = overlayRegions.Select(static region => region.Identity).ToArray();
            if (exception is not null)
            {
                throw exception;
            }

            return Task.FromResult(new ResolvedDemTextureSources(overlays));
        }

        public IReadOnlyList<TerrainTextureOverlay> CreateMapTileFallbackOverlays(
            IReadOnlyList<DemTerrainOverlayRegion> overlayRegions)
        {
            _ = overlayRegions;
            return overlays;
        }
    }
}

