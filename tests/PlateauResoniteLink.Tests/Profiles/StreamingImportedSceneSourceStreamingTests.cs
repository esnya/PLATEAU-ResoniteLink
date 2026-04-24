using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using GeographicLib;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Tests.Application.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class StreamingImportedSceneSourceStreamingTests
{
    [Fact]
    public async Task ReadCityObjectsAsync_UsesStreamingPipelineWithoutInvokingCachedParseTask()
    {
        CoordinateReferenceSystem referenceSystem =
            CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        GeodeticPoint globalOriginPoint = new(35.0, 139.0, 0.0);
        bool parseTaskInvoked = false;

        SourceFilePipeline bldgPipeline = CreatePipeline(
            new SourceFileDescriptor("udx/bldg/53394525/building.gml", "bldg", "53394525", RequiresMeshAreaFilter: false),
            [CreateParsedCityObject("bldg", "building", "Building", referenceSystem, lodLevel: 1)],
            streamFactory: static (sourceFile, cityObjects, beforeYield, cancellationToken) =>
                StreamSingleParsedCityObjectAsync(sourceFile, cityObjects, beforeYield, cancellationToken),
            parseTaskFactory: () =>
            {
                parseTaskInvoked = true;
                throw new InvalidOperationException("cached parse task should not be used in streaming path");
            });

        StreamingImportedSceneSource source = CreateSource(
            referenceSystem,
            globalOriginPoint,
            [bldgPipeline]);

        List<ImportedCityObject> yieldedObjects = [];
        await foreach (ImportedCityObject cityObject in source.ReadCityObjectsAsync())
        {
            yieldedObjects.Add(cityObject);
        }

        Assert.False(parseTaskInvoked);
        ImportedCityObject yielded = Assert.Single(yieldedObjects);
        Assert.Equal("bldg", yielded.PackageName);
    }

    [Fact]
    public async Task ReadCityObjectsAsync_CompletesAfterDelayedDemPipelineIsReleased()
    {
        CoordinateReferenceSystem referenceSystem =
            CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        GeodeticPoint globalOriginPoint = new(35.0, 139.0, 0.0);
        TaskCompletionSource demReleaseSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

        SourceFilePipeline bldgPipeline = CreatePipeline(
            new SourceFileDescriptor("udx/bldg/53394525/building.gml", "bldg", "53394525", RequiresMeshAreaFilter: false),
            [CreateParsedCityObject("bldg", "building", "Building", referenceSystem, lodLevel: 1)]);
        SourceFilePipeline tranPipeline = CreatePipeline(
            new SourceFileDescriptor("udx/tran/53394525/road.gml", "tran", "53394525", RequiresMeshAreaFilter: false),
            [CreateParsedCityObject("tran", "road", "Road", referenceSystem, lodLevel: 1)]);
        SourceFilePipeline demPipeline = CreatePipeline(
            new SourceFileDescriptor("udx/dem/53394525/terrain.gml", "dem", "53394525", RequiresMeshAreaFilter: false),
            [CreateParsedCityObject("dem", "terrain", "Terrain", referenceSystem, lodLevel: 1)],
            beforeYield: demReleaseSignal.Task);

        ImportedSceneSourceSnapshot readResult = new(
            new ImportedSceneSourceDataset(
                new EmptyDatasetContentSource(),
                [
                    bldgPipeline.SourceFile.RelativePath,
                    tranPipeline.SourceFile.RelativePath,
                    demPipeline.SourceFile.RelativePath,
                ],
                ["bldg", "dem", "tran"],
                [],
                ["53394525"]),
            new ImportedSceneSourceContext(
                [bldgPipeline, tranPipeline, demPipeline],
                globalOriginPoint));

        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: DatasetLocation.Local("/tmp/streaming"),
            PackageNames: ["bldg", "dem", "tran"]);
        ImportedSceneMetadata metadata = new(
            SchemaVersion: "3.0",
            SceneName: "test",
            Request: request,
            SourceDataset: new PlateauSourceDataset(
                ["bldg", "dem", "tran"],
                [
                    bldgPipeline.SourceFile.RelativePath,
                    tranPipeline.SourceFile.RelativePath,
                    demPipeline.SourceFile.RelativePath,
                ],
                []),
            Attribution: new Attribution(
                new LicenseMetadata(false, string.Empty, string.Empty, string.Empty),
                []),
            GeodeticOrigin: new GeodeticOrigin(globalOriginPoint.Latitude, globalOriginPoint.Longitude, globalOriginPoint.Altitude));
        RecordingGeometryProjector geometryProjector = new();

        StreamingImportedSceneSource source = new(
            metadata,
            request,
            readResult,
            geometryProjector,
            new StubDemTextureSourcePolicy(),
            new PassthroughImportedObjectUnitOptimizer());
        List<ImportedCityObject> yieldedObjects = [];
        Task collectTask = Task.Run(
            async () =>
            {
                await foreach (ImportedCityObject cityObject in source.ReadCityObjectsAsync())
                {
                    yieldedObjects.Add(cityObject);
                }
            });
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        Assert.False(collectTask.IsCompleted);

        demReleaseSignal.TrySetResult();
        await collectTask;

        Assert.Equal(3, yieldedObjects.Count);
        Assert.Contains(geometryProjector.Calls, static packageName => packageName == "bldg");
        Assert.Contains(geometryProjector.Calls, static packageName => packageName == "tran");
        Assert.Contains(geometryProjector.Calls, static packageName => packageName == "dem");
        Assert.Contains(yieldedObjects, static cityObject => cityObject.PackageName == "bldg");
        Assert.Contains(yieldedObjects, static cityObject => cityObject.PackageName == "tran");
        Assert.Contains(yieldedObjects, static cityObject => cityObject.PackageName == "dem");
    }

    private static SourceFilePipeline CreatePipeline(
        SourceFileDescriptor sourceFile,
        BootstrapParsedCityObject[] cityObjects,
        Task? beforeYield = null,
        Func<SourceFileDescriptor, BootstrapParsedCityObject[], Task?, CancellationToken, IAsyncEnumerable<BootstrapParsedCityObject>>? streamFactory = null,
        Func<Task<ParsedSourceFileResult>>? parseTaskFactory = null)
    {
        return new SourceFilePipeline(
            sourceFile,
            parseTaskFactory ?? (() => CreateParsedSourceFileResultAsync(sourceFile, cityObjects, beforeYield)),
            streamFactory is null
                ? null
                : cancellationToken => streamFactory(sourceFile, cityObjects, beforeYield, cancellationToken));
    }

    private static async Task<ParsedSourceFileResult> CreateParsedSourceFileResultAsync(
        SourceFileDescriptor sourceFile,
        BootstrapParsedCityObject[] cityObjects,
        Task? beforeYield)
    {
        if (beforeYield is not null)
        {
            await beforeYield;
        }

        return new ParsedSourceFileResult(
            sourceFile,
            cityObjects,
            cityObjects.Length == 0 ? null : cityObjects[0].ReferenceSystem,
            string.Equals(sourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
                ? DemSourceBootstrapSupport.CreateTerrainHeightTriangles(cityObjects)
                : [],
            TimeSpan.Zero);
    }

    private static async IAsyncEnumerable<BootstrapParsedCityObject> StreamSingleParsedCityObjectAsync(
        SourceFileDescriptor sourceFile,
        IEnumerable<BootstrapParsedCityObject> cityObjects,
        Task? beforeYield,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _ = sourceFile;
        if (beforeYield is not null)
        {
            await beforeYield.WaitAsync(cancellationToken);
        }

        foreach (BootstrapParsedCityObject cityObject in cityObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return cityObject;
        }
    }

    private static StreamingImportedSceneSource CreateSource(
        CoordinateReferenceSystem referenceSystem,
        GeodeticPoint globalOriginPoint,
        IReadOnlyList<SourceFilePipeline> sourceFilePipelines)
    {
        string[] packageNames = sourceFilePipelines
            .Select(static pipeline => pipeline.SourceFile.PackageName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            Source: DatasetLocation.Local("/tmp/streaming"),
            PackageNames: packageNames);
        ImportedSceneMetadata metadata = new(
            SchemaVersion: "3.0",
            SceneName: "test",
            Request: request,
            SourceDataset: new PlateauSourceDataset(
                packageNames,
                sourceFilePipelines.Select(static pipeline => pipeline.SourceFile.RelativePath).ToArray(),
                []),
            Attribution: new Attribution(
                new LicenseMetadata(false, string.Empty, string.Empty, string.Empty),
                []),
            GeodeticOrigin: new GeodeticOrigin(globalOriginPoint.Latitude, globalOriginPoint.Longitude, globalOriginPoint.Altitude));
        ImportedSceneSourceSnapshot readResult = new(
            new ImportedSceneSourceDataset(
                new EmptyDatasetContentSource(),
                sourceFilePipelines.Select(static pipeline => pipeline.SourceFile.RelativePath).ToArray(),
                packageNames,
                [],
                ["53394525"]),
            new ImportedSceneSourceContext(
                sourceFilePipelines,
                globalOriginPoint));

        return new StreamingImportedSceneSource(
            metadata,
            request,
            readResult,
            new RecordingGeometryProjector(),
            new StubDemTextureSourcePolicy(),
            new PassthroughImportedObjectUnitOptimizer());
    }

    private sealed class StubDemTextureSourcePolicy : IDemTextureSourcePolicy
    {
        public Task<ResolvedDemTextureSources> ResolveAsync(
            PlateauImportRequest request,
            IReadOnlyList<DemTerrainOverlayRegion> overlayRegions,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = overlayRegions;
            return Task.FromResult(new ResolvedDemTextureSources([]));
        }

        public IReadOnlyList<TerrainTextureOverlay> CreateMapTileFallbackOverlays(
            IReadOnlyList<DemTerrainOverlayRegion> overlayRegions)
        {
            _ = overlayRegions;
            return [];
        }
    }

    private static BootstrapParsedCityObject CreateParsedCityObject(
        string packageName,
        string objectKey,
        string displayName,
        CoordinateReferenceSystem referenceSystem,
        int? lodLevel)
    {
        BootstrapParsedRing exteriorRing = new(
            $"{objectKey}-ring",
            [
                new GeodeticPoint(35.0000, 139.0000, 0.0),
                new GeodeticPoint(35.0000, 139.0010, 0.0),
                new GeodeticPoint(35.0010, 139.0010, 2.0),
                new GeodeticPoint(35.0000, 139.0000, 0.0),
            ],
            UVs: null);
        BootstrapParsedSurface surface = new(
            $"{objectKey}-polygon",
            BootstrapParsedSurfaceSemantic.Ground,
            exteriorRing,
            [],
            new ColorRgba(1.0, 1.0, 1.0, 1.0),
            TexturePayload: null);

        return new BootstrapParsedCityObject(
            SlotKey: objectKey,
            DisplayName: displayName,
            PackageName: packageName,
            ActualMeshCode: "53394525",
            LodLevel: lodLevel,
            Surfaces: [surface],
            ReferenceSystem: referenceSystem,
            SourceFileRelativePath: $"udx/{packageName}/53394525/{objectKey}.gml",
            SharedAcrossMeshCodes: false);
    }

    private sealed class RecordingGeometryProjector : ICityGmlGeometryProjector
    {
        private readonly ConcurrentQueue<string> calls = [];

        public IReadOnlyCollection<string> Calls => calls.ToArray();

        public IEnumerable<ImportedCityObject> ProjectCityObjects(
            CachedSourceFileDescriptor sourceFile,
            CoordinateReferenceSystem referenceSystem,
            GeodeticPoint globalOriginPoint,
            LocalCartesian? globalCartesian,
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
            foreach (BootstrapParsedCityObject cityObject in sourceFile.CityObjects)
            {
                if (predicate is not null && !predicate(cityObject))
                {
                    continue;
                }

                calls.Enqueue(cityObject.PackageName);
                yield return new ImportedCityObject(
                    cityObject.SlotKey,
                    cityObject.DisplayName,
                    cityObject.PackageName,
                    cityObject.ActualMeshCode,
                    cityObject.LodLevel,
                    new Transform3D(new Float3(0.0, 0.0, 0.0)),
                    new TriangleMeshGeometry(
                        new ImportedMesh(
                            [
                                new MeshVertex(new Float3(0.0, 0.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(0.0, 0.0)),
                                new MeshVertex(new Float3(1.0, 0.0, 0.0), new Float3(0.0, 1.0, 0.0), new Float2(1.0, 0.0)),
                                new MeshVertex(new Float3(0.0, 0.0, 1.0), new Float3(0.0, 1.0, 0.0), new Float2(0.0, 1.0)),
                            ],
                            [new MeshSubmesh(0, $"{cityObject.PackageName}_material", [0, 1, 2])])),
                    [],
                    SourceFileRelativePath: sourceFile.RelativePath);
            }
        }
    }

    private sealed class EmptyDatasetContentSource : IPlateauDatasetContentSource
    {
        public string SourcePath => "/tmp/streaming";

        public IReadOnlyList<string> EnumerateFiles() => [];

        public bool FileExists(string relativePath) => false;

        public string? ResolveRelativePath(string baseRelativePath, string candidatePath) => null;

        public ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
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
