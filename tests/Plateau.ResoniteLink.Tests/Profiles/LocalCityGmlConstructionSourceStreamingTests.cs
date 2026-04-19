using System.Diagnostics.CodeAnalysis;

using GeographicLib;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Profiles;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class LocalCityGmlConstructionSourceStreamingTests
{
    private static readonly ICityGmlCommonMaterialEnumerator CommonMaterialEnumerator =
        new LocalCityGmlCommonMaterialEnumerator(new DefaultMaterialResolver());

    [Fact]
    public async Task ReadCityObjectsAsync_AllowsNonDemFilesToAdvanceBeforeDelayedDemPipelineCompletes()
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
            Source: PlateauImportSource.Local("/tmp/streaming"),
            PackageNames: ["bldg", "dem", "tran"]);
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
            geometryProjector,
            CommonMaterialEnumerator);
        TaskCompletionSource<IReadOnlyList<string>> firstTwoPackagesObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<ResoniteConstructionCityObject> yieldedObjects = [];
        Task collectTask = Task.Run(
            async () =>
            {
                await foreach (ResoniteConstructionCityObject cityObject in source.ReadCityObjectsAsync())
                {
                    yieldedObjects.Add(cityObject);
                    if (yieldedObjects.Count == 2)
                    {
                        firstTwoPackagesObserved.TrySetResult(yieldedObjects.Select(static item => item.PackageName).ToArray());
                    }
                }
            });

        IReadOnlyList<string> firstTwoPackages = await firstTwoPackagesObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(firstTwoPackages, static packageName => Assert.NotEqual("dem", packageName));

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
        Task? beforeYield = null)
    {
        return new SourceFilePipeline(
            sourceFile,
            async () =>
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
                        ? LocalCityGmlDemBootstrapSupport.CreateTerrainHeightTriangles(cityObjects)
                        : [],
                    TimeSpan.Zero);
            });
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
            SourceUnitIdentity: $"{packageName}_{objectKey}_unit",
            SourceIdentity: $"{packageName}_{objectKey}",
            SharedAcrossMeshCodes: false);
    }

    private sealed class RecordingGeometryProjector : ICityGmlGeometryProjector
    {
        public List<string> Calls { get; } = [];

        public IEnumerable<ResoniteConstructionCityObject> MaterializeCityObjects(
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

                Calls.Add(cityObject.PackageName);
                yield return new ResoniteConstructionCityObject(
                    cityObject.SlotKey,
                    cityObject.DisplayName,
                    cityObject.PackageName,
                    cityObject.ActualMeshCode,
                    cityObject.LodLevel,
                    new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
                    new ResoniteImportedMesh(
                        [
                            new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                            new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                            new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 1.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                        ],
                        [new ResoniteMeshSubmesh(0, $"{cityObject.PackageName}_material", [0, 1, 2])]),
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

        public IReadOnlyList<string> EnumerateFiles() => [];

        public bool FileExists(string relativePath) => false;

        public ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
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
