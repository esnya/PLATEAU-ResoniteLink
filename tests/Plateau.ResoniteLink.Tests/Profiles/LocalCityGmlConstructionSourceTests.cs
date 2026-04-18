using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class LocalCityGmlConstructionSourceTests
{
    [Fact]
    public async Task ReadCityObjectsAsyncLimitsProducerConcurrency()
    {
        const int sourceFileCount = 20;
        TrackingGeometryProjector.Reset();
        PlateauImportRequest request = new(
            Dataset: "plateau-04100-sendai-shi-2024",
            MeshCode: "57402736",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: "/tmp/source.zip",
            ServerUri: null);
        LocalCityGmlConstructionSource source = new(
            CreateMetadata(request),
            request,
            CreateDocumentSet(sourceFileCount),
            new TrackingGeometryProjector());

        List<ResoniteConstructionCityObject> cityObjects = [];
        await foreach (ResoniteConstructionCityObject cityObject in source.ReadCityObjectsAsync())
        {
            cityObjects.Add(cityObject);
        }

        Assert.Equal(sourceFileCount, cityObjects.Count);
        Assert.All(
            cityObjects,
            static cityObject => Assert.Equal("test-unit", cityObject.SourceUnitKey));
        Assert.InRange(
            TrackingGeometryProjector.MaxObservedConcurrency,
            1,
            LocalCityGmlConstructionSource.MaxConcurrentCityObjectProducers);
    }

    private static ResoniteConstructionMetadata CreateMetadata(PlateauImportRequest request)
    {
        return new ResoniteConstructionMetadata(
            SchemaVersion: "3.0",
            WorldName: "test-world",
            Request: request,
            SourceDataset: new PlateauSourceDataset([], [], [], []),
            Attribution: new ResoniteAttribution(
                new ResoniteLicenseComponentMetadata(
                    RequireCredit: true,
                    CreditText: "credit",
                    LicenseName: "license",
                    LicenseUrl: "https://example.invalid"),
                []),
            LocalOrigin: new ResoniteLocalOrigin(35.0, 139.0, 0.0));
    }

    private static LocalCityGmlDocumentSet CreateDocumentSet(int sourceFileCount)
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        SourceFilePipeline[] sourceFiles = Enumerable.Range(0, sourceFileCount)
            .Select(index => new SourceFilePipeline(
                new SourceFileDescriptor($"udx/bldg/file-{index:000}.gml", "bldg", "57402736", RequiresMeshAreaFilter: false),
                () => Task.FromResult(
                    new ParsedSourceFileResult(
                        new SourceFileDescriptor($"udx/bldg/file-{index:000}.gml", "bldg", "57402736", RequiresMeshAreaFilter: false),
                        [CreateParsedCityObject(index, referenceSystem)],
                        referenceSystem,
                        [],
                        TimeSpan.Zero))))
            .ToArray();

        return new LocalCityGmlDocumentSet(
            new EmptyDatasetContentSource(),
            sourceFiles.Select(static pipeline => pipeline.SourceFile.RelativePath).ToArray(),
            ["bldg"],
            [],
            ["57402736"],
            sourceFiles,
            [],
            referenceSystem,
            new GeodeticPoint(35.0, 139.0, 0.0),
            terrainHeightSampler: null);
    }

    private static BootstrapParsedCityObject CreateParsedCityObject(int index, CoordinateReferenceSystem referenceSystem)
    {
        return new BootstrapParsedCityObject(
            SlotKey: $"slot-{index:000}",
            DisplayName: $"slot-{index:000}",
            PackageName: "bldg",
            ActualMeshCode: "57402736",
            LodLevel: 1,
            Surfaces: [],
            ReferenceSystem: referenceSystem,
            SourceFileRelativePath: $"udx/bldg/file-{index:000}.gml",
            SourceUnitIdentity: "test-unit",
            SourceIdentity: $"test-unit:slot-{index:000}",
            SharedAcrossMeshCodes: false);
    }

    private sealed class TrackingGeometryProjector : ICityGmlGeometryProjector
    {
        private static int currentConcurrency;
        private static int maxObservedConcurrency;

        public static int MaxObservedConcurrency => maxObservedConcurrency;

        public static void Reset()
        {
            currentConcurrency = 0;
            maxObservedConcurrency = 0;
        }

        public IEnumerable<ResoniteConstructionCityObject> MaterializeCityObjects(
            CachedSourceFileDescriptor sourceFile,
            CoordinateReferenceSystem referenceSystem,
            GeodeticPoint globalOriginPoint,
            GeographicLib.LocalCartesian? globalCartesian,
            IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
            IReadOnlyList<LocalCityGmlResonitePlanBuilder.MeshCodeArea> requestedMeshAreas,
            PlateauImportRequest request,
            Func<BootstrapParsedCityObject, bool>? predicate = null)
        {
            _ = referenceSystem;
            _ = globalOriginPoint;
            _ = globalCartesian;
            _ = demTerrainTextureOverlays;
            _ = requestedMeshAreas;
            _ = request;
            _ = predicate;
            int concurrency = Interlocked.Increment(ref currentConcurrency);
            UpdateMaxConcurrency(concurrency);

            try
            {
                Thread.Sleep(20);
                BootstrapParsedCityObject parsedCityObject = Assert.Single(sourceFile.CityObjects);
                yield return new ResoniteConstructionCityObject(
                    SlotKey: parsedCityObject.SlotKey,
                    DisplayName: parsedCityObject.DisplayName,
                    PackageName: parsedCityObject.PackageName,
                    ActualMeshCode: parsedCityObject.ActualMeshCode,
                    LodLevel: parsedCityObject.LodLevel,
                    Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
                    Mesh: new ResoniteImportedMesh([], []),
                    Materials: [],
                    SourceObjectKey: parsedCityObject.SourceIdentity,
                    SourceUnitKey: parsedCityObject.SourceUnitIdentity,
                    SourceFileRelativePath: parsedCityObject.SourceFileRelativePath);
            }
            finally
            {
                Interlocked.Decrement(ref currentConcurrency);
            }
        }

        private static void UpdateMaxConcurrency(int concurrency)
        {
            while (true)
            {
                int observed = maxObservedConcurrency;
                if (concurrency <= observed)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref maxObservedConcurrency, concurrency, observed) == observed)
                {
                    return;
                }
            }
        }
    }

    private sealed class EmptyDatasetContentSource : IPlateauDatasetContentSource
    {
        public string SourcePath => "/tmp/source.zip";

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
