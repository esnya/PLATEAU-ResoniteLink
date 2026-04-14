using GeographicLib;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class LocalCityGmlConstructionSourceStreamingTests
{
    [Fact]
    public async Task ReadCityObjectsAsyncAllowsNonTerrainObjectsToAdvanceBeforeDemBootstrapCompletes()
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

        LocalCityGmlDocumentSet documentSet = new(
            new EmptyDatasetContentSource(),
            [
                bldgPipeline.SourceFile.RelativePath,
                tranPipeline.SourceFile.RelativePath,
                demPipeline.SourceFile.RelativePath,
            ],
            ["bldg", "dem", "tran"],
            [],
            ["53394525"],
            [bldgPipeline, tranPipeline, demPipeline],
            [],
            referenceSystem,
            globalOriginPoint,
            terrainHeightSampler: null);

        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: "/tmp/streaming",
            PackageNames: ["bldg", "dem", "tran"],
            ServerUri: null);
        ResoniteConstructionMetadata metadata = new(
            SchemaVersion: "3.0",
            WorldName: "test",
            Request: request,
            SourceDataset: new PlateauSourceDataset(
                ["bldg", "dem", "tran"],
                [
                    bldgPipeline.SourceFile.RelativePath,
                    tranPipeline.SourceFile.RelativePath,
                    demPipeline.SourceFile.RelativePath,
                ],
                []),
            Attribution: new ResoniteAttribution(
                new ResoniteLicenseComponentMetadata(false, string.Empty, string.Empty, string.Empty),
                []),
            LocalOrigin: new ResoniteLocalOrigin(globalOriginPoint.Latitude, globalOriginPoint.Longitude, globalOriginPoint.Altitude));
        RecordingGeometryProjector geometryProjector = new();

        LocalCityGmlConstructionSource source = new(
            metadata,
            request,
            documentSet,
            geometryProjector);

        await using IAsyncEnumerator<ResoniteConstructionCityObject> enumerator = source.ReadCityObjectsAsync().GetAsyncEnumerator();

        Task<bool> firstMoveTask = enumerator.MoveNextAsync().AsTask();
        Assert.Same(firstMoveTask, await Task.WhenAny(firstMoveTask, Task.Delay(TimeSpan.FromSeconds(1))));
        Assert.True(await firstMoveTask);
        Assert.NotEqual("Terrain", enumerator.Current.DisplayName);

        Task<bool> secondMoveTask = enumerator.MoveNextAsync().AsTask();
        Assert.Same(secondMoveTask, await Task.WhenAny(secondMoveTask, Task.Delay(200)));

        demReleaseSignal.TrySetResult();

        Assert.True(await secondMoveTask);
        Assert.NotEqual("Terrain", enumerator.Current.DisplayName);

        List<ResoniteConstructionCityObject> yieldedObjects = [enumerator.Current];
        while (await enumerator.MoveNextAsync())
        {
            yieldedObjects.Add(enumerator.Current);
        }

        Assert.Equal(3, yieldedObjects.Count);
        Assert.False(geometryProjector.Calls.Any(static call => call.TerrainSamplerPresent));
        Assert.Contains(yieldedObjects, static cityObject => cityObject.PackageName is "dem");
        Assert.Contains(geometryProjector.Calls, static call => call.PackageName == "bldg");
        Assert.Contains(geometryProjector.Calls, static call => call.PackageName == "tran");
        Assert.Contains(geometryProjector.Calls, static call => call.PackageName == "dem");
    }

    private static SourceFilePipeline CreatePipeline(
        SourceFileDescriptor sourceFile,
        BootstrapParsedCityObject[] cityObjects,
        Task? beforeYield = null)
    {
        return new SourceFilePipeline(
            sourceFile,
            () => Task.FromResult(new ParsedSourceFileResult(
                sourceFile,
                cityObjects,
                cityObjects.Length == 0 ? null : cityObjects[0].ReferenceSystem,
                string.Equals(sourceFile.PackageName, "dem", StringComparison.OrdinalIgnoreCase)
                    ? LocalCityGmlDemBootstrapSupport.CreateTerrainHeightTriangles(cityObjects)
                    : [],
                TimeSpan.Zero)),
            cancellationToken => StreamCityObjectsAsync(cityObjects, beforeYield, cancellationToken));
    }

    private static async IAsyncEnumerable<BootstrapParsedCityObject> StreamCityObjectsAsync(
        IReadOnlyList<BootstrapParsedCityObject> cityObjects,
        Task? beforeYield,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
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
            new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            TexturePath: null);

        return new BootstrapParsedCityObject(
            SlotKey: objectKey,
            DisplayName: displayName,
            PackageName: packageName,
            ActualMeshCode: "53394525",
            LodLevel: lodLevel,
            Surfaces: [surface],
            ReferenceSystem: referenceSystem,
            SourceFileRelativePath: $"udx/{packageName}/53394525/{objectKey}.gml",
            SourceUnitIdentity: $"{packageName}_{objectKey}_unit",
            SourceIdentity: $"{packageName}_{objectKey}",
            SharedAcrossMeshCodes: false);
    }

    private sealed class RecordingGeometryProjector : ICityGmlGeometryProjector
    {
        public List<(string PackageName, bool TerrainSamplerPresent)> Calls { get; } = [];

        public IEnumerable<ResoniteConstructionCityObject> MaterializeCityObjects(
            CachedSourceFileDescriptor sourceFile,
            CoordinateReferenceSystem referenceSystem,
            GeodeticPoint globalOriginPoint,
            LocalCartesian? globalCartesian,
            IReadOnlyList<TerrainTextureOverlay> demTerrainTextureOverlays,
            TerrainHeightSampler? terrainHeightSampler,
            PlateauImportRequest request,
            Func<BootstrapParsedCityObject, bool>? predicate = null)
        {
            foreach (BootstrapParsedCityObject cityObject in sourceFile.CityObjects)
            {
                if (predicate is not null && !predicate(cityObject))
                {
                    continue;
                }

                Calls.Add((cityObject.PackageName, terrainHeightSampler is not null));
                yield return new ResoniteConstructionCityObject(
                    cityObject.SlotKey,
                    cityObject.DisplayName,
                    cityObject.PackageName,
                    cityObject.ActualMeshCode,
                    cityObject.LodLevel,
                    new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
                    new ResoniteImportedMesh(
                        [
                            new ResoniteMeshVertex(
                                new ResoniteFloat3(0.0, 0.0, 0.0),
                                new ResoniteFloat3(0.0, 1.0, 0.0),
                                new ResoniteFloat2(0.0, 0.0)),
                            new ResoniteMeshVertex(
                                new ResoniteFloat3(1.0, 0.0, 0.0),
                                new ResoniteFloat3(0.0, 1.0, 0.0),
                                new ResoniteFloat2(1.0, 0.0)),
                            new ResoniteMeshVertex(
                                new ResoniteFloat3(0.0, 0.0, 1.0),
                                new ResoniteFloat3(0.0, 1.0, 0.0),
                                new ResoniteFloat2(0.0, 1.0)),
                        ],
                        [
                            new ResoniteMeshSubmesh(
                                0,
                                $"{cityObject.PackageName}_material",
                                [0, 1, 2]),
                        ]),
                    [],
                    SourceObjectKey: cityObject.SourceIdentity,
                    SourceUnitKey: cityObject.SourceUnitIdentity,
                    SourceFileRelativePath: sourceFile.RelativePath);
            }
        }
    }

    private sealed class EmptyDatasetContentSource : IPlateauDatasetContentSource
    {
        public string SourcePath => "/tmp/streaming";

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
