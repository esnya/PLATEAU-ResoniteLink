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
        DefaultImportServiceFactory factory = new(
            datasetResolverFactory,
            sceneImportTargetFactory,
            documentReader,
            constructionSourceFactory);

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
            1,
            enableMeshBake,
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

        public Task EnsureConnectedAsync(PlateauImportRequest request, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task BeginAsync(SceneBuildRequest request, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ProcessCityObjectAsync(ImportedCityObject cityObject, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> CompleteAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<string>>(["stub://resonite/location"]);
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

            ResoniteConstructionMetadata metadata = new(
                "3.0",
                $"PLATEAU {request.Dataset} {request.MeshCode}",
                request,
                new PlateauSourceDataset(["bldg"], [], [], []),
                new ResoniteAttribution(
                    new ResoniteLicenseComponentMetadata(true, "credit", "license", "https://example.invalid"),
                    []),
                new ResoniteLocalOrigin(35.0, 139.0, 0.0));

            return Task.FromResult<IResoniteConstructionSource>(new StubConstructionSource(metadata));
        }
    }

    private sealed class StubConstructionSource(ResoniteConstructionMetadata metadata) : IResoniteConstructionSource
    {
        public ResoniteConstructionMetadata Metadata { get; } = metadata;

        [Obsolete]
        public IAsyncEnumerable<ResoniteMaterialBinding> ReadCommonMaterialsAsync(CancellationToken cancellationToken = default)
        {
            return AsyncEnumerable.Empty<ResoniteMaterialBinding>();
        }

        public IEnumerable<ResoniteConstructionCityObject> ReadCityObjects()
        {
            return [];
        }

        public async IAsyncEnumerable<ResoniteConstructionCityObject> ReadCityObjectsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ResoniteConstructionCityObject(
                "object-1",
                "Object 1",
                "bldg",
                "53394525",
                1,
                new ResoniteTransform(new ResoniteFloat3(0, 0, 0)),
                new ResoniteImportedMesh(
                    [
                        new ResoniteMeshVertex(new ResoniteFloat3(0, 0, 0), new ResoniteFloat3(0, 1, 0), new ResoniteFloat2(0, 0)),
                        new ResoniteMeshVertex(new ResoniteFloat3(1, 0, 0), new ResoniteFloat3(0, 1, 0), new ResoniteFloat2(1, 0)),
                        new ResoniteMeshVertex(new ResoniteFloat3(0, 0, 1), new ResoniteFloat3(0, 1, 0), new ResoniteFloat2(0, 1)),
                    ],
                    [
                        new ResoniteMeshSubmesh(0, "mat", [0, 1, 2]),
                    ]),
                [
                    new ResoniteMaterialBinding(
                        "mat",
                        new ResoniteColor(1, 1, 1, 1),
                        ResoniteMaterialType.Standard,
                        null,
                        ResoniteTextureSourceKind.Dataset,
                        ResoniteMaterialProjection.Uv,
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
