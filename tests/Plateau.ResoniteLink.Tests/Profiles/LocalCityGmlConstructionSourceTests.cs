using System.Collections.Concurrent;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class LocalCityGmlConstructionSourceTests
{
    private static readonly ICityGmlCommonMaterialEnumerator CommonMaterialEnumerator =
        new LocalCityGmlCommonMaterialEnumerator(new DefaultMaterialResolver());

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
            new TrackingGeometryProjector(),
            CommonMaterialEnumerator);

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

    [Fact]
    public async Task ReadCityObjectsAsyncDoesNotPassDemOverlaysToNonDemSourceFiles()
    {
        PlateauImportRequest request = new(
            Dataset: "plateau-04100-sendai-shi-2024",
            MeshCode: "57402736",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: "/tmp/source.zip",
            ServerUri: null,
            PackageNames: ["bldg", "dem"]);
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            UrlTemplate: "https://example.invalid/{z}/{x}/{y}.png",
            ZoomLevel: 18,
            GeographicBounds: new GeographicRectangle(35.0, 35.1, 139.0, 139.1),
            MaxTextureSize: 1024,
            LicenseMode: TerrainTextureLicenseMode.PlateauOrthoWithGsiFallback);
        OverlayRecordingGeometryProjector geometryProjector = new();
        LocalCityGmlConstructionSource source = new(
            CreateMetadata(request, [overlay]),
            request,
            CreateDocumentSet(
                [
                    new SourceFileDescriptor("udx/bldg/file-000.gml", "bldg", "57402736", RequiresMeshAreaFilter: false),
                    new SourceFileDescriptor("udx/dem/file-001.gml", "dem", "57402736", RequiresMeshAreaFilter: false),
                ]),
            geometryProjector,
            CommonMaterialEnumerator);

        List<ResoniteConstructionCityObject> cityObjects = [];
        await foreach (ResoniteConstructionCityObject cityObject in source.ReadCityObjectsAsync())
        {
            cityObjects.Add(cityObject);
        }

        Assert.Equal(2, cityObjects.Count);
        Assert.Equal(0, geometryProjector.OverlayCountsByPackage["bldg"]);
        Assert.True(geometryProjector.OverlayCountsByPackage.ContainsKey("dem"));
    }

    [Fact]
    public async Task ReadCityObjectsAsyncAllowsDemSourceFilesWhenBootstrapMetadataIsEmpty()
    {
        PlateauImportRequest request = new(
            Dataset: "plateau-04100-sendai-shi-2024",
            MeshCode: "57402736",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: "/tmp/source.zip",
            ServerUri: null,
            PackageNames: ["dem"]);
        OverlayRecordingGeometryProjector geometryProjector = new();
        LocalCityGmlConstructionSource source = new(
            CreateMetadata(request),
            request,
            CreateDocumentSet(
                [
                    new SourceFileDescriptor("udx/dem/file-001.gml", "dem", "57402736", RequiresMeshAreaFilter: false),
                ]),
            geometryProjector,
            CommonMaterialEnumerator);

        List<ResoniteConstructionCityObject> cityObjects = [];
        await foreach (ResoniteConstructionCityObject cityObject in source.ReadCityObjectsAsync())
        {
            cityObjects.Add(cityObject);
        }

        Assert.Single(cityObjects);
        Assert.Equal(0, geometryProjector.OverlayCountsByPackage["dem"]);
    }

    private static ResoniteConstructionMetadata CreateMetadata(
        PlateauImportRequest request,
        IReadOnlyList<TerrainTextureOverlay>? terrainTextureOverlays = null)
    {
        return new ResoniteConstructionMetadata(
            SchemaVersion: "3.0",
            WorldName: "test-world",
            Request: request,
            SourceDataset: new PlateauSourceDataset([], [], terrainTextureOverlays ?? [], []),
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
        SourceFileDescriptor[] sourceFiles = Enumerable.Range(0, sourceFileCount)
            .Select(index => new SourceFileDescriptor(
                $"udx/bldg/file-{index:000}.gml",
                "bldg",
                "57402736",
                RequiresMeshAreaFilter: false))
            .ToArray();
        return CreateDocumentSet(sourceFiles);
    }

    private static LocalCityGmlDocumentSet CreateDocumentSet(IReadOnlyList<SourceFileDescriptor> sourceFiles)
    {
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        SourceFilePipeline[] pipelines = sourceFiles
            .Select((sourceFile, index) => new SourceFilePipeline(
                sourceFile,
                () => Task.FromResult(
                    new ParsedSourceFileResult(
                        sourceFile,
                        [CreateParsedCityObject(index, sourceFile, referenceSystem)],
                        referenceSystem,
                        [],
                        TimeSpan.Zero))))
            .ToArray();

        return new LocalCityGmlDocumentSet(
            new EmptyDatasetContentSource(),
            pipelines.Select(static pipeline => pipeline.SourceFile.RelativePath).ToArray(),
            pipelines.Select(static pipeline => pipeline.SourceFile.PackageName).Distinct(StringComparer.Ordinal).ToArray(),
            [],
            ["57402736"],
            pipelines,
            [],
            referenceSystem,
            new GeodeticPoint(35.0, 139.0, 0.0),
            terrainHeightSampler: null);
    }

    private static BootstrapParsedCityObject CreateParsedCityObject(
        int index,
        SourceFileDescriptor sourceFile,
        CoordinateReferenceSystem referenceSystem)
    {
        return new BootstrapParsedCityObject(
            SlotKey: $"slot-{index:000}",
            DisplayName: $"slot-{index:000}",
            PackageName: sourceFile.PackageName,
            ActualMeshCode: sourceFile.MatchedMeshCode,
            LodLevel: 1,
            Surfaces: [],
            ReferenceSystem: referenceSystem,
            SourceFileRelativePath: sourceFile.RelativePath,
            SourceUnitIdentity: "test-unit",
            SourceIdentity: $"{sourceFile.PackageName}:slot-{index:000}",
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
            IReadOnlyList<MeshCodeBounds> requestedMeshAreas,
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

    private sealed class OverlayRecordingGeometryProjector : ICityGmlGeometryProjector
    {
        public ConcurrentDictionary<string, int> OverlayCountsByPackage { get; } = new(StringComparer.Ordinal);

        public IEnumerable<ResoniteConstructionCityObject> MaterializeCityObjects(
            CachedSourceFileDescriptor sourceFile,
            CoordinateReferenceSystem referenceSystem,
            GeodeticPoint globalOriginPoint,
            GeographicLib.LocalCartesian? globalCartesian,
            IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
            IReadOnlyList<MeshCodeBounds> requestedMeshAreas,
            PlateauImportRequest request,
            Func<BootstrapParsedCityObject, bool>? predicate = null)
        {
            _ = referenceSystem;
            _ = globalOriginPoint;
            _ = globalCartesian;
            _ = requestedMeshAreas;
            _ = request;

            BootstrapParsedCityObject parsedCityObject = Assert.Single(sourceFile.CityObjects);
            if (predicate is not null && !predicate(parsedCityObject))
            {
                yield break;
            }

            OverlayCountsByPackage[parsedCityObject.PackageName] = demTerrainTextureOverlays.Count;
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
                SourceFileRelativePath: sourceFile.RelativePath);
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
