using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class ImportServiceFactoryTests
{
    [Fact]
    public async Task CreateBuildsRunScopedServicesAndPassesTargetOptionsThrough()
    {
        StubPlateauDatasetSourceResolverFactory datasetResolverFactory = new();
        StubSceneImportTargetFactory sceneImportTargetFactory = new();
        StubCityGmlDocumentReader documentReader = new();
        StubConstructionSourceFactory constructionSourceFactory = new();
        IArchiveFileLayoutPolicy archiveFileLayoutPolicy = new ArchiveFileLayoutPolicy();
        DefaultImportServiceFactory factory = new(
            datasetResolverFactory,
            sceneImportTargetFactory,
            documentReader,
            constructionSourceFactory,
            archiveFileLayoutPolicy);

        BuildCommandOptions firstOptions = CreateOptions("53394525", enableMeshBake: true);
        BuildCommandOptions secondOptions = CreateOptions("53394526", enableMeshBake: false);

        PlateauImportService firstService = factory.Create(firstOptions, progressReporter: null);
        PlateauImportService secondService = factory.Create(secondOptions, progressReporter: null);

        Assert.NotSame(firstService, secondService);
        Assert.Equal(2, datasetResolverFactory.CreatedResolvers.Count);
        Assert.Equal(2, sceneImportTargetFactory.CreatedTargets.Count);
        Assert.Equal([firstOptions, secondOptions], sceneImportTargetFactory.CapturedOptions);

        ImportExecutionResult firstResult = await firstService.ExecuteAsync(firstOptions.Request, firstOptions.WorkRoot);
        ImportExecutionResult secondResult = await secondService.ExecuteAsync(secondOptions.Request, secondOptions.WorkRoot);

        Assert.Equal("PLATEAU tokyo23ku 53394525", firstResult.Metadata.SceneName);
        Assert.Equal("PLATEAU tokyo23ku 53394526", secondResult.Metadata.SceneName);
        Assert.True(sceneImportTargetFactory.CreatedTargets.All(static target => target.DisposeCallCount == 1));
        Assert.Equal(2, documentReader.ReadCallCount);
        Assert.Equal(2, constructionSourceFactory.CreateWithDocumentSetCallCount);
    }

    private static BuildCommandOptions CreateOptions(string meshCode, bool enableMeshBake)
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: meshCode,
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: TestData.GetFixturePath("LocalPlateauDataset"),
            ServerUri: null);

        return new BuildCommandOptions(
            request,
            "local",
            new Uri("ws://localhost:12345/"),
            4,
            PlateauImportMemoryProfile.Large,
            enableMeshBake,
            TerrainTileCacheRoot: null,
            DisableTerrainTileCache: false,
            EnableSendMetrics: false,
            VerboseLogging: false);
    }

    private sealed class StubPlateauDatasetSourceResolverFactory : IPlateauDatasetSourceResolverFactory
    {
        public List<StubPlateauDatasetSourceResolver> CreatedResolvers { get; } = [];

        public IPlateauDatasetSourceResolver Create()
        {
            StubPlateauDatasetSourceResolver resolver = new();
            CreatedResolvers.Add(resolver);
            return resolver;
        }
    }

    private sealed class StubPlateauDatasetSourceResolver : IPlateauDatasetSourceResolver
    {
        public Task<ValidatedPlateauImportRequest> ResolveAsync(
            ValidatedPlateauImportRequest request,
            string workRoot,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(request);
        }
    }

    private sealed class StubSceneImportTargetFactory : ISceneImportTargetFactory
    {
        public List<BuildCommandOptions> CapturedOptions { get; } = [];
        public List<StubSceneImportTarget> CreatedTargets { get; } = [];

        public ISceneImportTarget Create(BuildCommandOptions options, Action<string>? progressReporter)
        {
            CapturedOptions.Add(options);
            StubSceneImportTarget target = new();
            CreatedTargets.Add(target);
            return target;
        }
    }

    private sealed class StubSceneImportTarget : ISceneImportTarget
    {
        public int DisposeCallCount { get; private set; }

        public async Task<SceneImportExecutionResult> ExecuteAsync(
            SceneImportExecutionPlan plan,
            IAsyncEnumerable<ImportedCityObject> cityObjects,
            CancellationToken cancellationToken = default)
        {
            _ = plan;
            await foreach (ImportedCityObject cityObject in cityObjects.WithCancellation(cancellationToken))
            {
                _ = cityObject;
            }

            return new SceneImportExecutionResult(["stub://resonite/location"], 1);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCallCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubCityGmlDocumentReader : ICityGmlDocumentReader
    {
        public int ReadCallCount { get; private set; }

        public Task<LocalCityGmlDocumentSet> ReadAsync(
            PlateauImportRequest request,
            Action<string>? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            ReadCallCount++;
            return Task.FromResult(new LocalCityGmlDocumentSet(
                new StubDatasetContentSource(request.LocalSourcePath!),
                [],
                ["bldg"],
                [],
                ["53394525"],
                [],
                [],
                CoordinateReferenceSystem.Parse("http://www.opengis.net/def/crs/EPSG/0/6697"),
                new GeodeticPoint(35.0, 139.0, 0.0),
                terrainHeightSampler: null));
        }
    }

    private sealed class StubConstructionSourceFactory : IResoniteConstructionSourceFactory
    {
        public int CreateWithDocumentSetCallCount { get; private set; }

        public Task<IResoniteConstructionSource> CreateAsync(
            PlateauImportRequest request,
            Action<string>? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IResoniteConstructionSource> CreateAsync(
            PlateauImportRequest request,
            LocalCityGmlDocumentSet documentSet,
            Action<string>? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            CreateWithDocumentSetCallCount++;

            ConstructionMetadata metadata = new(
                "3.0",
                $"PLATEAU {request.Dataset} {request.MeshCode}",
                request,
                new PlateauSourceDataset(["bldg"], [], [], []),
                new Attribution(
                    new LicenseMetadata(true, "credit", "license", "https://example.invalid"),
                    []),
                new LocalOrigin(35.0, 139.0, 0.0));

            return Task.FromResult<IResoniteConstructionSource>(new StubConstructionSource(metadata));
        }
    }

    private sealed class StubConstructionSource(ConstructionMetadata metadata) : IResoniteConstructionSource
    {
        public ConstructionMetadata Metadata { get; } = metadata;

        [Obsolete]
        public IAsyncEnumerable<MaterialBinding> ReadCommonMaterialsAsync(CancellationToken cancellationToken = default)
        {
            return AsyncEnumerable.Empty<MaterialBinding>();
        }

        public IEnumerable<ImportedCityObject> ReadCityObjects()
        {
            return [];
        }

        public async IAsyncEnumerable<ImportedCityObject> ReadCityObjectsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ImportedCityObject(
                "object-1",
                "Object 1",
                "bldg",
                "53394525",
                1,
                new Transform3d(new Float3(0, 0, 0)),
                new TriangleMeshGeometry(new ImportedMesh(
                    [
                        new MeshVertex(new Float3(0, 0, 0), new Float3(0, 1, 0), new Float2(0, 0)),
                        new MeshVertex(new Float3(1, 0, 0), new Float3(0, 1, 0), new Float2(1, 0)),
                        new MeshVertex(new Float3(0, 0, 1), new Float3(0, 1, 0), new Float2(0, 1)),
                    ],
                    [
                        new MeshSubmesh(0, "mat", [0, 1, 2]),
                    ])),
                [
                    new MaterialBinding(
                        "mat",
                        new ColorRgba(1, 1, 1, 1),
                        MaterialType.Standard,
                        null,
                        TextureSourceKind.Dataset,
                        MaterialProjection.Uv,
                        null,
                        [0]),
                ]);

            await Task.CompletedTask;
        }
    }

    private sealed class StubDatasetContentSource(string sourcePath) : IPlateauDatasetContentSource
    {
        public string SourcePath { get; } = sourcePath;

        public IReadOnlyList<string> EnumerateFiles()
        {
            return [];
        }

        public bool FileExists(string relativePath)
        {
            return false;
        }

        public ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<string> MaterializeFileAsync(
            string relativePath,
            string outputRoot,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
