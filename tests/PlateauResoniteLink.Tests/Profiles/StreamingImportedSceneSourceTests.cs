using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Tests.Application.Importing;

namespace PlateauResoniteLink.Tests.Profiles;

public sealed class StreamingImportedSceneSourceTests
{
    [Fact]
    public async Task ReadCityObjectsAsyncLimitsProducerConcurrency()
    {
        const int sourceFileCount = 20;
        TrackingGeometryProjector.Reset();
        PlateauImportRequest request = new(
            Dataset: "plateau-04100-sendai-shi-2024",
            MeshCode: "57402736",
            CityGmlSource: DatasetLocation.Local("/tmp/source.zip"));
        StreamingImportedSceneSource source = new(
            CreateMetadata(request),
            request,
            CreateReadResult(sourceFileCount),
            new TrackingGeometryProjector(),
            new StubDemTextureSourcePolicy(),
            new PassthroughImportedObjectUnitOptimizer());

        List<ImportedCityObject> cityObjects = [];
        await foreach (ImportedCityObject cityObject in source.ReadCityObjectsAsync())
        {
            cityObjects.Add(cityObject);
        }

        Assert.Equal(sourceFileCount, cityObjects.Count);
        Assert.All(
            cityObjects,
            static cityObject => Assert.Equal("bldg", cityObject.PackageName));
        Assert.InRange(
            TrackingGeometryProjector.MaxObservedConcurrency,
            1,
            StreamingImportedSceneSource.MaxConcurrentCityObjectProducers);
    }

    [Fact]
    public async Task ReadCityObjectsAsyncPassesDiscoveryDemOverlaysToDemAndBuildingSourceFiles()
    {
        PlateauImportRequest request = new(
            Dataset: "plateau-04100-sendai-shi-2024",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local("/tmp/source.zip"),

            PackageNames: ["bldg", "dem"]);
        TerrainTextureOverlay overlay = new(
            PackageName: "dem",
            UrlTemplate: "https://example.invalid/{z}/{x}/{y}.png",
            ZoomLevel: 18,
            GeographicBounds: new GeographicRectangle(35.0, 35.1, 139.0, 139.1),
            MaxTextureSize: 1024,
            LicenseMode: TerrainTextureLicenseMode.PlateauOrthoWithGsiFallback);
        OverlayRecordingGeometryProjector geometryProjector = new();
        StreamingImportedSceneSource source = new(
            CreateMetadata(request, [overlay]),
            request,
            CreateReadResult(
                [
                    new SourceFileDescriptor("udx/bldg/file-000.gml", "bldg", "53394525", RequiresMeshCodeBoundsFilter: false),
                    new SourceFileDescriptor("udx/dem/file-001.gml", "dem", "57402736", RequiresMeshCodeBoundsFilter: false),
                ],
                [overlay]),
            geometryProjector,
            new StubDemTextureSourcePolicy(),
            new PassthroughImportedObjectUnitOptimizer());

        List<ImportedCityObject> cityObjects = [];
        await foreach (ImportedCityObject cityObject in source.ReadCityObjectsAsync())
        {
            cityObjects.Add(cityObject);
        }

        Assert.Equal(2, cityObjects.Count);
        Assert.Equal(1, geometryProjector.OverlayCountsByPackage["bldg"]);
        Assert.Equal(1, geometryProjector.OverlayCountsByPackage["dem"]);
        Assert.Same(overlay, geometryProjector.LastOverlayByPackage["bldg"]);
        Assert.Same(overlay, geometryProjector.LastOverlayByPackage["dem"]);
    }

    [Fact]
    public async Task ReadCityObjectsAsyncUsesExplicitDemTextureSourceOverDiscoveryProjectionOverlays()
    {
        PlateauImportRequest request = new(
            Dataset: "plateau-04100-sendai-shi-2024",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local("/tmp/source.zip"),
            PackageNames: ["bldg", "dem"],
            DemTextureSource: DatasetLocation.Local("C:\\ortho"));
        TerrainTextureOverlay discoveryOverlay = new(
            PackageName: "dem",
            UrlTemplate: "https://example.invalid/discovery/{z}/{x}/{y}.png",
            ZoomLevel: 18,
            GeographicBounds: new GeographicRectangle(35.0, 36.0, 139.0, 140.0),
            MaxTextureSize: 1024,
            LicenseMode: TerrainTextureLicenseMode.PlateauOrthoWithGsiFallback);
        TerrainTextureOverlay explicitRasterOverlay = new(
            PackageName: "dem",
            GeographicBounds: new GeographicRectangle(35.0, 35.01, 139.0, 139.01),
            MaxTextureSize: 1024,
            Sources:
            [
                new TerrainTextureGeoReferencedRasterSource(
                    "C:\\ortho\\terrain.tif",
                    new GeoReferencedRasterMetadata(
                        new GeographicRectangle(35.0, 35.01, 139.0, 139.01),
                        "EPSG:4326",
                        1.0,
                        1.0)),
            ]);
        OverlayRecordingGeometryProjector geometryProjector = new();
        StubDemTextureSourcePolicy demTextureSourcePolicy = new(explicitRasterOverlay);
        StreamingImportedSceneSource source = new(
            CreateMetadata(request, [discoveryOverlay]),
            request,
            CreateReadResult(
                [
                    new SourceFileDescriptor("udx/bldg/file-000.gml", "bldg", "53394525", RequiresMeshCodeBoundsFilter: false),
                    new SourceFileDescriptor("udx/dem/file-001.gml", "dem", "53394525", RequiresMeshCodeBoundsFilter: false),
                ],
                [discoveryOverlay]),
            geometryProjector,
            demTextureSourcePolicy,
            new PassthroughImportedObjectUnitOptimizer());

        await source.ValidateBeforeSinkSetupAsync();
        await source.ReadCityObjectsAsync().ToListAsync();

        Assert.Equal(1, demTextureSourcePolicy.ResolveOverlayRegionsCallCount);
        Assert.Same(explicitRasterOverlay, geometryProjector.LastOverlayByPackage["bldg"]);
        Assert.Same(explicitRasterOverlay, geometryProjector.LastOverlayByPackage["dem"]);
    }

    [Fact]
    public async Task ReadCityObjectsAsyncRebuildsDiscoveryDemOverlaysWhenParsedDemCoverageMisses()
    {
        PlateauImportRequest request = new(
            Dataset: "plateau-04100-sendai-shi-2024",
            MeshCode: "53394525",
            CityGmlSource: DatasetLocation.Local("/tmp/source.zip"),
            PackageNames: ["bldg", "dem"]);
        TerrainTextureOverlay staleDiscoveryOverlay = new(
            PackageName: "dem",
            UrlTemplate: "https://example.invalid/stale/{z}/{x}/{y}.png",
            ZoomLevel: 18,
            GeographicBounds: new GeographicRectangle(36.0, 36.1, 140.0, 140.1),
            MaxTextureSize: 1024,
            LicenseMode: TerrainTextureLicenseMode.PlateauOrthoWithGsiFallback);
        TerrainTextureOverlay rebuiltOverlay = new(
            PackageName: "dem",
            GeographicBounds: new GeographicRectangle(35.0, 35.03, 139.0, 139.03),
            MaxTextureSize: 1024,
            Sources:
            [
                new TerrainTextureTileSource("https://tiles.example/rebuilt/{z}/{x}/{y}.png", 17),
            ]);
        OverlayRecordingGeometryProjector geometryProjector = new();
        StubDemTextureSourcePolicy demTextureSourcePolicy = new(rebuiltOverlay);
        StreamingImportedSceneSource source = new(
            CreateMetadata(request),
            request,
            CreateReadResult(
                [
                    new SourceFileDescriptor("udx/bldg/file-000.gml", "bldg", "53394525", RequiresMeshCodeBoundsFilter: false),
                    new SourceFileDescriptor("udx/dem/file-001.gml", "dem", "53394525", RequiresMeshCodeBoundsFilter: false),
                ],
                [staleDiscoveryOverlay],
                selectedMeshCodes: ["53394525"]),
            geometryProjector,
            demTextureSourcePolicy,
            new PassthroughImportedObjectUnitOptimizer());

        await source.ReadCityObjectsAsync().ToListAsync();

        Assert.Equal(1, demTextureSourcePolicy.ResolveOverlayRegionsCallCount);
        Assert.Same(rebuiltOverlay, geometryProjector.LastOverlayByPackage["bldg"]);
        Assert.Same(rebuiltOverlay, geometryProjector.LastOverlayByPackage["dem"]);
    }

    [Fact]
    public async Task ReadCityObjectsAsyncResolvesSceneDemOverlaysForBuildingProjectionByParsedDemCoverage()
    {
        PlateauImportRequest request = new(
            Dataset: "plateau-04100-sendai-shi-2024",
            MeshCode: "57402736",
            CityGmlSource: DatasetLocation.Local("/tmp/source.zip"),

            PackageNames: ["bldg", "dem"]);
        TerrainTextureOverlay fallbackOverlay = new(
            PackageName: "dem",
            GeographicBounds: new GeographicRectangle(35.0, 35.01, 139.0, 139.01),
            MaxTextureSize: 1024,
            Sources:
            [
                new TerrainTextureTileSource("https://tiles.example/fallback/{z}/{x}/{y}.png", 17),
            ]);
        OverlayRecordingGeometryProjector geometryProjector = new();
        StubDemTextureSourcePolicy demTextureSourcePolicy = new(fallbackOverlay);
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        SourceFileDescriptor buildingSourceFile = new("udx/bldg/file-000.gml", "bldg", "53394525", RequiresMeshCodeBoundsFilter: false);
        SourceFileDescriptor demSourceFile = new("udx/dem/file-001.gml", "dem", "53394525", RequiresMeshCodeBoundsFilter: false);
        StreamingImportedSceneSource source = new(
            CreateMetadata(request),
            request,
            CreateReadResult(
                [
                    new SourceFilePipeline(
                        buildingSourceFile,
                        () => Task.FromResult(
                            new ParsedSourceFileResult(
                                buildingSourceFile,
                                [CreateParsedCityObject(0, buildingSourceFile, referenceSystem)],
                                referenceSystem,
                                [],
                                TimeSpan.Zero))),
                    new SourceFilePipeline(
                        demSourceFile,
                        () => Task.FromResult(
                            new ParsedSourceFileResult(
                                demSourceFile,
                                [CreateParsedCityObjectInMesh53394525(demSourceFile, referenceSystem)],
                                referenceSystem,
                                [],
                                TimeSpan.Zero))),
                ],
                selectedMeshCodes: ["53394525"]),
            geometryProjector,
            demTextureSourcePolicy,
            new PassthroughImportedObjectUnitOptimizer());

        List<ImportedCityObject> cityObjects = [];
        await foreach (ImportedCityObject cityObject in source.ReadCityObjectsAsync())
        {
            cityObjects.Add(cityObject);
        }

        Assert.Equal(2, cityObjects.Count);
        Assert.Equal(1, geometryProjector.OverlayCountsByPackage["bldg"]);
        Assert.Equal(1, geometryProjector.OverlayCountsByPackage["dem"]);
        Assert.Same(fallbackOverlay, geometryProjector.LastOverlayByPackage["bldg"]);
        Assert.Same(fallbackOverlay, geometryProjector.LastOverlayByPackage["dem"]);
        Assert.Equal(1, demTextureSourcePolicy.ResolveOverlayRegionsCallCount);
        Assert.Contains(
            demTextureSourcePolicy.OverlayRegionIdentityCalls,
            static identities => identities.SequenceEqual(["53394525"]));
    }

    [Fact]
    public async Task ReadCityObjectsAsyncUsesDemTextureSourcePolicyForFallbackOverlayComposition()
    {
        PlateauImportRequest request = new(
            Dataset: "plateau-04100-sendai-shi-2024",
            MeshCode: "57402736",
            CityGmlSource: DatasetLocation.Local("/tmp/source.zip"),

            PackageNames: ["dem"]);
        TerrainTextureOverlay fallbackOverlay = new(
            PackageName: "dem",
            GeographicBounds: new GeographicRectangle(35.0, 35.01, 139.0, 139.01),
            MaxTextureSize: 1024,
            Sources:
            [
                new TerrainTextureTileSource("https://tiles.example/fallback/{z}/{x}/{y}.png", 17),
            ]);
        OverlayRecordingGeometryProjector geometryProjector = new();
        StubDemTextureSourcePolicy demTextureSourcePolicy = new(fallbackOverlay);
        StreamingImportedSceneSource source = new(
            CreateMetadata(request),
            request,
            CreateReadResult(
                [
                    new SourceFileDescriptor("udx/dem/file-001.gml", "dem", "57402736", RequiresMeshCodeBoundsFilter: false),
                ]),
            geometryProjector,
            demTextureSourcePolicy,
            new PassthroughImportedObjectUnitOptimizer());

        List<ImportedCityObject> cityObjects = [];
        await foreach (ImportedCityObject cityObject in source.ReadCityObjectsAsync())
        {
            cityObjects.Add(cityObject);
        }

        Assert.Single(cityObjects);
        Assert.NotNull(demTextureSourcePolicy.LastOverlayRegionIdentities);
        Assert.Single(demTextureSourcePolicy.LastOverlayRegionIdentities!);
        Assert.Equal(
            "https://tiles.example/fallback/{z}/{x}/{y}.png",
            geometryProjector.LastOverlayByPackage["dem"]!.GetRequiredPrimaryTileSource().UrlTemplate);
    }

    [Fact]
    public async Task ReadCityObjectsAsyncDoesNotEagerParseAllDemFilesForMapTileFallbackOverlays()
    {
        PlateauImportRequest request = new(
            Dataset: "plateau-04100-sendai-shi-2024",
            MeshCode: "57402736",
            CityGmlSource: DatasetLocation.Local("/tmp/source.zip"),
            PackageNames: ["dem"]);
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        int parseTaskCallCount = 0;
        SourceFilePipeline[] pipelines = Enumerable.Range(0, 3)
            .Select(index =>
            {
                SourceFileDescriptor sourceFile = new(
                    $"udx/dem/file-{index:000}.gml",
                    "dem",
                    "57402736",
                    RequiresMeshCodeBoundsFilter: false);
                return new SourceFilePipeline(
                    sourceFile,
                    () =>
                    {
                        Interlocked.Increment(ref parseTaskCallCount);
                        return Task.FromResult(
                            new ParsedSourceFileResult(
                                sourceFile,
                                [CreateParsedCityObject(index, sourceFile, referenceSystem)],
                                referenceSystem,
                                [],
                                TimeSpan.Zero));
                    },
                    streamFactory: cancellationToken => StreamSingleParsedCityObjectAsync(
                        index,
                        sourceFile,
                        referenceSystem,
                        cancellationToken));
            })
            .ToArray();
        StubDemTextureSourcePolicy demTextureSourcePolicy = new(
            new TerrainTextureOverlay(
                PackageName: "dem",
                GeographicBounds: new GeographicRectangle(35.0, 35.01, 139.0, 139.01),
                MaxTextureSize: 1024,
                Sources:
                [
                    new TerrainTextureTileSource("https://tiles.example/fallback/{z}/{x}/{y}.png", 17),
                ]));
        StreamingImportedSceneSource source = new(
            CreateMetadata(request),
            request,
            CreateReadResult(pipelines),
            new TrackingGeometryProjector(),
            demTextureSourcePolicy,
            new PassthroughImportedObjectUnitOptimizer());

        await source.ReadCityObjectsAsync().ToListAsync();

        Assert.Equal(0, parseTaskCallCount);
        Assert.Equal(1, demTextureSourcePolicy.ResolveOverlayRegionsCallCount);

        static async IAsyncEnumerable<ParsedCityObject> StreamSingleParsedCityObjectAsync(
            int index,
            SourceFileDescriptor sourceFile,
            CoordinateReferenceSystem referenceSystem,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return CreateParsedCityObject(index, sourceFile, referenceSystem);
            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ReadCityObjectsAsyncResolvesDemFallbackOverlaysOncePerScene()
    {
        PlateauImportRequest request = new(
            Dataset: "plateau-04100-sendai-shi-2024",
            MeshCode: "57402736",
            CityGmlSource: DatasetLocation.Local("/tmp/source.zip"),

            PackageNames: ["dem"]);
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        SourceFileDescriptor sourceFile = new("udx/dem/file-001.gml", "dem", "57402736", RequiresMeshCodeBoundsFilter: false);
        SourceFileDescriptor secondSourceFile = new("udx/dem/file-002.gml", "dem", "57402736", RequiresMeshCodeBoundsFilter: false);
        StubDemTextureSourcePolicy demTextureSourcePolicy = new(
            new TerrainTextureOverlay(
                PackageName: "dem",
                GeographicBounds: new GeographicRectangle(35.0, 35.01, 139.0, 139.01),
                MaxTextureSize: 1024,
                Sources:
                [
                    new TerrainTextureTileSource("https://tiles.example/fallback/{z}/{x}/{y}.png", 17),
                ]));
        StreamingImportedSceneSource source = new(
            CreateMetadata(request),
            request,
            CreateReadResult(
                [
                    new SourceFilePipeline(
                        sourceFile,
                        () => Task.FromResult(
                            new ParsedSourceFileResult(
                                sourceFile,
                                [
                                    CreateParsedCityObject(0, sourceFile, referenceSystem),
                                    CreateParsedCityObject(1, sourceFile, referenceSystem),
                                ],
                                referenceSystem,
                                [],
                                TimeSpan.Zero)),
                        streamFactory: cancellationToken => StreamParsedCityObjects(sourceFile, referenceSystem, cancellationToken)),
                    new SourceFilePipeline(
                        secondSourceFile,
                        () => Task.FromResult(
                            new ParsedSourceFileResult(
                                secondSourceFile,
                                [
                                    CreateParsedCityObject(2, secondSourceFile, referenceSystem),
                                ],
                                referenceSystem,
                                [],
                                TimeSpan.Zero)),
                        streamFactory: cancellationToken => StreamParsedCityObjects(secondSourceFile, referenceSystem, cancellationToken)),
                ]),
            new TrackingGeometryProjector(),
            demTextureSourcePolicy,
            new PassthroughImportedObjectUnitOptimizer());

        await source.ReadCityObjectsAsync().ToListAsync();

        Assert.Equal(1, demTextureSourcePolicy.ResolveOverlayRegionsCallCount);

        static async IAsyncEnumerable<ParsedCityObject> StreamParsedCityObjects(
            SourceFileDescriptor sourceFile,
            CoordinateReferenceSystem referenceSystem,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return CreateParsedCityObject(0, sourceFile, referenceSystem);
            cancellationToken.ThrowIfCancellationRequested();
            yield return CreateParsedCityObject(1, sourceFile, referenceSystem);
            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ReadCityObjectsAsyncReusesExplicitDemTextureSourcePreflightForProjectionOverlays()
    {
        PlateauImportRequest request = new(
            Dataset: "plateau-04100-sendai-shi-2024",
            MeshCode: "57402736",
            CityGmlSource: DatasetLocation.Local("/tmp/source.zip"),
            PackageNames: ["bldg", "dem"],
            DemTextureSource: DatasetLocation.Local("C:\\ortho"));
        TerrainTextureOverlay explicitRasterOverlay = new(
            PackageName: "dem",
            GeographicBounds: new GeographicRectangle(35.0, 35.01, 139.0, 139.01),
            MaxTextureSize: 1024,
            Sources:
            [
                new TerrainTextureGeoReferencedRasterSource(
                    "C:\\ortho\\terrain.tif",
                    new GeoReferencedRasterMetadata(
                        new GeographicRectangle(35.0, 35.01, 139.0, 139.01),
                        "EPSG:4326",
                        1.0,
                        1.0)),
            ]);
        OverlayRecordingGeometryProjector geometryProjector = new();
        StubDemTextureSourcePolicy demTextureSourcePolicy = new(explicitRasterOverlay);
        StreamingImportedSceneSource source = new(
            CreateMetadata(request),
            request,
            CreateReadResult(
                [
                    new SourceFileDescriptor("udx/bldg/file-000.gml", "bldg", "57402736", RequiresMeshCodeBoundsFilter: false),
                    new SourceFileDescriptor("udx/dem/file-001.gml", "dem", "57402736", RequiresMeshCodeBoundsFilter: false),
                ]),
            geometryProjector,
            demTextureSourcePolicy,
            new PassthroughImportedObjectUnitOptimizer());

        await source.ValidateBeforeSinkSetupAsync();
        await source.ReadCityObjectsAsync().ToListAsync();

        Assert.Equal(1, demTextureSourcePolicy.ResolveOverlayRegionsCallCount);
        Assert.Same(explicitRasterOverlay, geometryProjector.LastOverlayByPackage["bldg"]);
        Assert.Same(explicitRasterOverlay, geometryProjector.LastOverlayByPackage["dem"]);
    }

    [Fact]
    public async Task ValidateBeforeSinkSetupAsyncLimitsParentMeshExplicitDemValidationToParsedDemCoverage()
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "533945",
            CityGmlSource: DatasetLocation.Local("/tmp/source.zip"),
            PackageNames: ["bldg", "dem"],
            DemTextureSource: DatasetLocation.Local("C:\\ortho"));
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        SourceFileDescriptor buildingSourceFile = new("udx/bldg/file-000.gml", "bldg", "53394525", RequiresMeshCodeBoundsFilter: true);
        SourceFileDescriptor demSourceFile = new("udx/dem/file-001.gml", "dem", "533945", RequiresMeshCodeBoundsFilter: true);
        TerrainTextureOverlay explicitRasterOverlay = new(
            PackageName: "dem",
            GeographicBounds: new GeographicRectangle(35.0, 35.01, 139.0, 139.01),
            MaxTextureSize: 1024,
            Sources:
            [
                new TerrainTextureGeoReferencedRasterSource(
                    "C:\\ortho\\terrain.tif",
                    new GeoReferencedRasterMetadata(
                        new GeographicRectangle(35.0, 35.01, 139.0, 139.01),
                        "EPSG:4326",
                        1.0,
                        1.0)),
            ]);
        StubDemTextureSourcePolicy demTextureSourcePolicy = new(explicitRasterOverlay);
        StreamingImportedSceneSource source = new(
            CreateMetadata(request),
            request,
            CreateReadResult(
                [
                    new SourceFilePipeline(
                        buildingSourceFile,
                        () => Task.FromResult(
                            new ParsedSourceFileResult(
                                buildingSourceFile,
                                [CreateParsedCityObject(0, buildingSourceFile, referenceSystem)],
                                referenceSystem,
                                [],
                                TimeSpan.Zero))),
                    new SourceFilePipeline(
                        demSourceFile,
                        () => Task.FromResult(
                            new ParsedSourceFileResult(
                                demSourceFile,
                                [CreateParsedCityObjectInMesh53394525(demSourceFile, referenceSystem)],
                                referenceSystem,
                                [],
                                TimeSpan.Zero))),
                ],
                selectedMeshCodes: ["533945"]),
            new OverlayRecordingGeometryProjector(),
            demTextureSourcePolicy,
            new PassthroughImportedObjectUnitOptimizer());

        await source.ValidateBeforeSinkSetupAsync();

        Assert.Equal(1, demTextureSourcePolicy.ResolveOverlayRegionsCallCount);
        Assert.Equal(["53394525"], demTextureSourcePolicy.LastOverlayRegionIdentities);
    }

    [Fact]
    public async Task ReadCityObjectsAsyncPreservesExplicitRasterWhenFallbackOverlayCoverageIsRebuilt()
    {
        PlateauImportRequest request = new(
            Dataset: "plateau-04100-sendai-shi-2024",
            MeshCode: "57402736",
            CityGmlSource: DatasetLocation.Local("/tmp/source.zip"),
            PackageNames: ["dem"],
            DemTextureSource: DatasetLocation.Local("C:\\ortho"));
        TerrainTextureOverlay explicitRasterOverlay = new(
            PackageName: "dem",
            GeographicBounds: new GeographicRectangle(35.0, 35.01, 139.0, 139.01),
            MaxTextureSize: 1024,
            Sources:
            [
                new TerrainTextureGeoReferencedRasterSource(
                    "C:\\ortho\\terrain.tif",
                    new GeoReferencedRasterMetadata(
                        new GeographicRectangle(35.0, 35.01, 139.0, 139.01),
                        "EPSG:4326",
                        1.0,
                        1.0)),
                new TerrainTextureTileSource("https://tiles.example/fallback/{z}/{x}/{y}.png", 17),
            ]);
        OverlayRecordingGeometryProjector geometryProjector = new();
        StubDemTextureSourcePolicy demTextureSourcePolicy = new(explicitRasterOverlay);
        StreamingImportedSceneSource source = new(
            CreateMetadata(request),
            request,
            CreateReadResult(
                [
                    new SourceFileDescriptor("udx/dem/file-001.gml", "dem", "57402736", RequiresMeshCodeBoundsFilter: false),
                ]),
            geometryProjector,
            demTextureSourcePolicy,
            new PassthroughImportedObjectUnitOptimizer());

        List<ImportedCityObject> cityObjects = [];
        await foreach (ImportedCityObject cityObject in source.ReadCityObjectsAsync())
        {
            cityObjects.Add(cityObject);
        }

        Assert.Single(cityObjects);
        TerrainTextureOverlay appliedOverlay = Assert.IsType<TerrainTextureOverlay>(geometryProjector.LastOverlayByPackage["dem"]);
        TerrainTextureGeoReferencedRasterSource rasterSource = Assert.Single(appliedOverlay.EnumerateGeoReferencedRasterSources());
        Assert.Equal("C:\\ortho\\terrain.tif", rasterSource.SourcePath);
    }

    [Fact]
    public async Task ReadCityObjectsAsyncPropagatesExplicitDemTextureSourceValidationFailureDuringStreaming()
    {
        PlateauImportRequest request = new(
            Dataset: "plateau-04100-sendai-shi-2024",
            MeshCode: "57402736",
            CityGmlSource: DatasetLocation.Local("/tmp/source.zip"),
            PackageNames: ["dem"],
            DemTextureSource: DatasetLocation.Local("C:\\ortho\\missing.tif"));
        PlateauImportValidationException expectedException = new(["invalid GeoTIFF source"]);
        StreamingImportedSceneSource source = new(
            CreateMetadata(request),
            request,
            CreateReadResult(
                [
                    new SourceFileDescriptor("udx/dem/file-001.gml", "dem", "57402736", RequiresMeshCodeBoundsFilter: false),
                ]),
            new TrackingGeometryProjector(),
            new ThrowingDemTextureSourcePolicy(expectedException),
            new PassthroughImportedObjectUnitOptimizer());

        PlateauImportValidationException actualException = await Assert.ThrowsAsync<PlateauImportValidationException>(
            () => source.ReadCityObjectsAsync().ToListAsync().AsTask());

        Assert.Same(expectedException, actualException);
    }

    [Fact]
    public async Task ReadCityObjectsAsyncCancelsWhileResolvingDemFallbackOverlays()
    {
        PlateauImportRequest request = new(
            Dataset: "plateau-04100-sendai-shi-2024",
            MeshCode: "57402736",
            CityGmlSource: DatasetLocation.Local("/tmp/source.zip"),

            PackageNames: ["dem"]);
        CoordinateReferenceSystem referenceSystem = CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697");
        SourceFileDescriptor sourceFile = new("udx/dem/file-001.gml", "dem", "57402736", RequiresMeshCodeBoundsFilter: false);
        StubDemTextureSourcePolicy demTextureSourcePolicy = new(delayOverlayResolutionUntilCancellation: true);
        StreamingImportedSceneSource source = new(
            CreateMetadata(request),
            request,
            CreateReadResult(
                [
                    new SourceFilePipeline(
                        sourceFile,
                        () => Task.FromResult(
                            new ParsedSourceFileResult(
                                sourceFile,
                                [CreateParsedCityObject(0, sourceFile, referenceSystem)],
                                referenceSystem,
                            [],
                            TimeSpan.Zero))),
                ]),
            new TrackingGeometryProjector(),
            demTextureSourcePolicy,
            new PassthroughImportedObjectUnitOptimizer());

        using CancellationTokenSource cancellationTokenSource = new();
        Task readTask = source.ReadCityObjectsAsync(cancellationTokenSource.Token).ToListAsync(cancellationTokenSource.Token).AsTask();
        await WaitForConditionAsync(
            () => demTextureSourcePolicy.ResolveOverlayRegionsCallCount == 1,
            TimeSpan.FromSeconds(5));
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => readTask);
        Assert.Equal(1, demTextureSourcePolicy.ResolveOverlayRegionsCallCount);
    }

    private static ImportedSceneMetadata CreateMetadata(
        PlateauImportRequest request,
        IReadOnlyList<TerrainTextureOverlay>? terrainTextureOverlays = null)
    {
        return new ImportedSceneMetadata(
            SchemaVersion: "3.0",
            SceneName: "test-world",
            Request: request,
            SourceDataset: new PlateauSourceDataset([], [], []),
            Attribution: new Attribution(
                new LicenseMetadata(
                    RequireCredit: true,
                    CreditText: "credit",
                    LicenseName: "license",
                    LicenseUrl: "https://example.invalid")),
            GeodeticOrigin: new GeodeticOrigin(35.0, 139.0, 0.0));
    }

    private static ImportedSceneSourceSnapshot CreateReadResult(int sourceFileCount)
    {
        SourceFileDescriptor[] sourceFiles = Enumerable.Range(0, sourceFileCount)
            .Select(index => new SourceFileDescriptor(
                $"udx/bldg/file-{index:000}.gml",
                "bldg",
                "57402736",
                RequiresMeshCodeBoundsFilter: false))
            .ToArray();
        return CreateReadResult(sourceFiles);
    }

    private static ImportedSceneSourceSnapshot CreateReadResult(
        IReadOnlyList<SourceFileDescriptor> sourceFiles,
        IReadOnlyList<TerrainTextureOverlay>? terrainTextureOverlays = null,
        IReadOnlyList<string>? selectedMeshCodes = null)
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

        return new ImportedSceneSourceSnapshot(
            new ImportedSceneSourceDataset(
                new EmptyDatasetContentSource(),
                pipelines.Select(static pipeline => pipeline.SourceFile.RelativePath).ToArray(),
                pipelines.Select(static pipeline => pipeline.SourceFile.PackageName).Distinct(StringComparer.Ordinal).ToArray(),
                terrainTextureOverlays ?? [],
                selectedMeshCodes ?? ["57402736"]),
            new ImportedSceneSourceContext(
                pipelines,
                new GeodeticPoint(35.0, 139.0, 0.0)));
    }

    private static ImportedSceneSourceSnapshot CreateReadResult(
        IReadOnlyList<SourceFilePipeline> pipelines,
        IReadOnlyList<TerrainTextureOverlay>? terrainTextureOverlays = null,
        IReadOnlyList<string>? selectedMeshCodes = null)
    {
        return new ImportedSceneSourceSnapshot(
            new ImportedSceneSourceDataset(
                new EmptyDatasetContentSource(),
                pipelines.Select(static pipeline => pipeline.SourceFile.RelativePath).ToArray(),
                pipelines.Select(static pipeline => pipeline.SourceFile.PackageName).Distinct(StringComparer.Ordinal).ToArray(),
                terrainTextureOverlays ?? [],
                selectedMeshCodes ?? ["57402736"]),
            new ImportedSceneSourceContext(
                pipelines.ToArray(),
                new GeodeticPoint(35.0, 139.0, 0.0)));
    }

    private static ParsedCityObject CreateParsedCityObject(
        int index,
        SourceFileDescriptor sourceFile,
        CoordinateReferenceSystem referenceSystem)
    {
        ParsedSurface[] surfaces = string.Equals(sourceFile.PackageName, "dem", StringComparison.Ordinal)
            ?
            [
                new ParsedSurface(
                    PolygonId: $"dem-surface-{index:000}",
                    Semantic: ParsedSurfaceSemantic.Ground,
                    ExteriorRing: new ParsedRing(
                        $"dem-ring-{index:000}",
                        [
                            new GeodeticPoint(35.01, 139.01, 0.0),
                            new GeodeticPoint(35.01, 139.02, 0.0),
                            new GeodeticPoint(35.02, 139.02, 0.0),
                            new GeodeticPoint(35.02, 139.01, 0.0),
                        ],
                        UVs: null),
                    InteriorRings: [],
                    BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
                    TexturePayload: null,
                    UsesGeneratedDemTexture: true),
            ]
            : [];

        return new ParsedCityObject(
            SlotKey: $"slot-{index:000}",
            DisplayName: $"slot-{index:000}",
            PackageName: sourceFile.PackageName,
            ActualMeshCode: sourceFile.MatchedMeshCode,
            LodLevel: 1,
            Surfaces: surfaces,
            ReferenceSystem: referenceSystem,
            SourceFileRelativePath: sourceFile.RelativePath,
            SharedAcrossMeshCodes: false);
    }

    private static ParsedCityObject CreateParsedCityObjectInMesh53394525(
        SourceFileDescriptor sourceFile,
        CoordinateReferenceSystem referenceSystem)
    {
        return new ParsedCityObject(
            SlotKey: "slot-53394525",
            DisplayName: "slot-53394525",
            PackageName: sourceFile.PackageName,
            ActualMeshCode: sourceFile.MatchedMeshCode,
            LodLevel: 1,
            Surfaces:
            [
                new ParsedSurface(
                    PolygonId: "dem-surface-53394525",
                    Semantic: ParsedSurfaceSemantic.Ground,
                    ExteriorRing: new ParsedRing(
                        "dem-ring-53394525",
                        [
                            new GeodeticPoint(35.684, 139.688, 0.0),
                            new GeodeticPoint(35.684, 139.689, 0.0),
                            new GeodeticPoint(35.685, 139.689, 0.0),
                            new GeodeticPoint(35.685, 139.688, 0.0),
                        ],
                        UVs: null),
                    InteriorRings: [],
                    BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
                    TexturePayload: null,
                    UsesGeneratedDemTexture: true),
            ],
            ReferenceSystem: referenceSystem,
            SourceFileRelativePath: sourceFile.RelativePath,
            SharedAcrossMeshCodes: false);
    }

    private static ParsedCityObject CreateRenderableParsedCityObject(
        SourceFileDescriptor sourceFile,
        CoordinateReferenceSystem referenceSystem)
    {
        return new ParsedCityObject(
            SlotKey: "slot-renderable",
            DisplayName: "slot-renderable",
            PackageName: sourceFile.PackageName,
            ActualMeshCode: sourceFile.MatchedMeshCode,
            LodLevel: 1,
            Surfaces:
            [
                new ParsedSurface(
                    PolygonId: "surface-renderable",
                    Semantic: ParsedSurfaceSemantic.Wall,
                    ExteriorRing: new ParsedRing(
                        "ring-renderable",
                        [
                            new GeodeticPoint(35.01, 139.01, 0.0),
                            new GeodeticPoint(35.01, 139.02, 0.0),
                            new GeodeticPoint(35.02, 139.02, 0.0),
                        ],
                        UVs:
                        [
                            new Float2(0.0, 0.0),
                            new Float2(1.0, 0.0),
                            new Float2(0.0, 1.0),
                        ]),
                    InteriorRings: [],
                    BaseColor: new ColorRgba(1.0, 1.0, 1.0, 1.0),
                    TexturePayload: null,
                    UsesGeneratedDemTexture: false),
            ],
            ReferenceSystem: referenceSystem,
            SourceFileRelativePath: sourceFile.RelativePath,
            SharedAcrossMeshCodes: false);
    }

    private static async Task WaitForConditionAsync(Func<bool> predicate, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.True(predicate(), "Timed out waiting for expected condition.");
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

        public IEnumerable<ImportedCityObject> ProjectCityObjects(
            CachedSourceFileDescriptor sourceFile,
            CoordinateReferenceSystem referenceSystem,
            GeodeticPoint globalOriginPoint,
            GeographicLib.LocalCartesian? globalCartesian,
            IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
            IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
            PlateauImportRequest request,
            Func<ParsedCityObject, bool>? predicate = null,
            Action<string>? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            _ = referenceSystem;
            _ = globalOriginPoint;
            _ = globalCartesian;
            _ = demTerrainTextureOverlays;
            _ = requestedMeshCodeBounds;
            _ = request;
            _ = predicate;
            _ = progressReporter;
            _ = cancellationToken;
            int concurrency = Interlocked.Increment(ref currentConcurrency);
            UpdateMaxConcurrency(concurrency);

            try
            {
                Thread.Sleep(20);
                ParsedCityObject parsedCityObject = Assert.Single(sourceFile.CityObjects);
                yield return new ImportedCityObject(
                    ObjectKey: parsedCityObject.SlotKey,
                    DisplayName: parsedCityObject.DisplayName,
                    PackageName: parsedCityObject.PackageName,
                    ActualMeshCode: parsedCityObject.ActualMeshCode,
                    LodLevel: parsedCityObject.LodLevel,
                    Transform: new Transform3D(new Float3(0.0, 0.0, 0.0)),
                    Geometry: new TriangleMeshGeometry(new ImportedMesh([], [])),
                    Materials: [],
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
        public ConcurrentDictionary<string, TerrainTextureOverlay?> LastOverlayByPackage { get; } = new(StringComparer.Ordinal);

        public IEnumerable<ImportedCityObject> ProjectCityObjects(
            CachedSourceFileDescriptor sourceFile,
            CoordinateReferenceSystem referenceSystem,
            GeodeticPoint globalOriginPoint,
            GeographicLib.LocalCartesian? globalCartesian,
            IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
            IReadOnlyList<MeshCodeBounds> requestedMeshCodeBounds,
            PlateauImportRequest request,
            Func<ParsedCityObject, bool>? predicate = null,
            Action<string>? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            _ = referenceSystem;
            _ = globalOriginPoint;
            _ = globalCartesian;
            _ = requestedMeshCodeBounds;
            _ = request;
            _ = progressReporter;
            _ = cancellationToken;

            ParsedCityObject parsedCityObject = Assert.Single(sourceFile.CityObjects);
            if (predicate is not null && !predicate(parsedCityObject))
            {
                yield break;
            }

            OverlayCountsByPackage[parsedCityObject.PackageName] = demTerrainTextureOverlays.Count;
            LastOverlayByPackage[parsedCityObject.PackageName] =
                demTerrainTextureOverlays.Count > 0 ? demTerrainTextureOverlays[0] : null;
            yield return new ImportedCityObject(
                ObjectKey: parsedCityObject.SlotKey,
                DisplayName: parsedCityObject.DisplayName,
                PackageName: parsedCityObject.PackageName,
                ActualMeshCode: parsedCityObject.ActualMeshCode,
                LodLevel: parsedCityObject.LodLevel,
                Transform: new Transform3D(new Float3(0.0, 0.0, 0.0)),
                Geometry: new TriangleMeshGeometry(new ImportedMesh([], [])),
                Materials: [],
                SourceFileRelativePath: sourceFile.RelativePath);
        }
    }

    private sealed class StubDemTextureSourcePolicy : IDemTextureSourcePolicy
    {
        private readonly TerrainTextureOverlay[] fallbackOverlays;
        private readonly bool delayOverlayResolutionUntilCancellation;

        public StubDemTextureSourcePolicy(
            params TerrainTextureOverlay[] fallbackOverlays)
            : this(false, fallbackOverlays)
        {
        }

        public StubDemTextureSourcePolicy(
            bool delayOverlayResolutionUntilCancellation,
            params TerrainTextureOverlay[] fallbackOverlays)
        {
            this.fallbackOverlays = fallbackOverlays;
            this.delayOverlayResolutionUntilCancellation = delayOverlayResolutionUntilCancellation;
        }

        public IReadOnlyList<string>? LastOverlayRegionIdentities { get; private set; }

        public ConcurrentQueue<IReadOnlyList<string>> OverlayRegionIdentityCalls { get; } = new();

        private int resolveOverlayRegionsCallCount;

        public int ResolveOverlayRegionsCallCount => resolveOverlayRegionsCallCount;

        public Task<ResolvedDemTextureSources> ResolveAsync(
            PlateauImportRequest request,
            IReadOnlyList<DemTerrainOverlayRegion> overlayRegions,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            Interlocked.Increment(ref resolveOverlayRegionsCallCount);
            LastOverlayRegionIdentities = overlayRegions.Select(static region => region.Identity).ToArray();
            OverlayRegionIdentityCalls.Enqueue(LastOverlayRegionIdentities);
            return ResolveOverlayRegionsCoreAsync(cancellationToken);
        }

        public IReadOnlyList<TerrainTextureOverlay> CreateMapTileFallbackOverlays(
            IReadOnlyList<DemTerrainOverlayRegion> overlayRegions)
        {
            LastOverlayRegionIdentities = overlayRegions.Select(static region => region.Identity).ToArray();
            OverlayRegionIdentityCalls.Enqueue(LastOverlayRegionIdentities);
            return fallbackOverlays;
        }

        private async Task<ResolvedDemTextureSources> ResolveOverlayRegionsCoreAsync(
            CancellationToken cancellationToken)
        {
            if (delayOverlayResolutionUntilCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new ResolvedDemTextureSources(fallbackOverlays);
        }
    }

    private sealed class ThrowingDemTextureSourcePolicy(PlateauImportValidationException exception) : IDemTextureSourcePolicy
    {
        public Task<ResolvedDemTextureSources> ResolveAsync(
            PlateauImportRequest request,
            IReadOnlyList<DemTerrainOverlayRegion> overlayRegions,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = overlayRegions;
            _ = cancellationToken;
            throw exception;
        }

        public IReadOnlyList<TerrainTextureOverlay> CreateMapTileFallbackOverlays(
            IReadOnlyList<DemTerrainOverlayRegion> overlayRegions)
        {
            _ = overlayRegions;
            throw exception;
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

        public string? ResolveRelativePath(string baseRelativePath, string candidatePath)
        {
            return null;
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
