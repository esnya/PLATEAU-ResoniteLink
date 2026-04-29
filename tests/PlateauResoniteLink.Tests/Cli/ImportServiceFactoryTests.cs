using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Cli;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Cli;

public sealed class ImportServiceFactoryTests
{
    [Fact]
    public async Task CreateBuildsRunScopedServicesAndPassesTargetOptionsThrough()
    {
        StubPlateauDatasetSourceResolverFactory datasetResolverFactory = new();
        StubSceneSinkFactory sceneImportTargetFactory = new();
        StubImportedSceneSourceFactory importedSceneSourceFactory = new();
        IArchiveFileLayoutPolicy archiveFileLayoutPolicy = new ArchiveFileLayoutPolicy();
        DefaultImportServiceFactory factory = new(
            datasetResolverFactory,
            sceneImportTargetFactory,
            importedSceneSourceFactory,
            new CommonMaterialCatalog(),
            archiveFileLayoutPolicy);

        ImportCommandOptions firstOptions = CreateOptions("53394525", enableMeshBake: true);
        ImportCommandOptions secondOptions = CreateOptions("53394526", enableMeshBake: false);

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
        Assert.Equal(2, importedSceneSourceFactory.CreateCallCount);
    }

    private static ImportCommandOptions CreateOptions(string meshCode, bool enableMeshBake)
    {
        PlateauImportRequest request = new(
            Dataset: "tokyo23ku",
            MeshCode: meshCode,
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: TestData.GetFixturePath("LocalPlateauDataset"),
            ServerUri: null);

        return new ImportCommandOptions(
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

    private sealed class StubSceneSinkFactory : ISceneSinkFactory
    {
        public List<ImportCommandOptions> CapturedOptions { get; } = [];
        public List<StubSceneImportSink> CreatedTargets { get; } = [];

        public ISceneSink Create(ImportCommandOptions options, Action<string>? progressReporter)
        {
            CapturedOptions.Add(options);
            StubSceneImportSink target = new();
            CreatedTargets.Add(target);
            return target;
        }
    }

    private sealed class StubSceneImportSink : ISceneSink
    {
        public int DisposeCallCount { get; private set; }

        public async Task<SceneImportExecutionResult> ExecuteAsync(
            SceneImportExecutionPlan plan,
            IAsyncEnumerable<ImportedObjectUnit> objectUnits,
            CancellationToken cancellationToken = default)
        {
            _ = plan;
            await foreach (ImportedObjectUnit objectUnit in objectUnits.WithCancellation(cancellationToken))
            {
                _ = objectUnit;
            }

            return new SceneImportExecutionResult(["stub://resonite/location"], 1);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCallCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubImportedSceneSourceFactory : IImportedSceneSourceFactory
    {
        public int CreateCallCount { get; private set; }

        public Task<IImportedSceneSource> CreateAsync(
            PlateauImportRequest request,
            Action<string>? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            CreateCallCount++;

            ImportedSceneMetadata metadata = new(
                "3.0",
                $"PLATEAU {request.Dataset} {request.MeshCode}",
                request,
                new PlateauSourceDataset(["bldg"], [], []),
                new Attribution(
                    new LicenseMetadata(true, "credit", "license", "https://example.invalid"),
                    []),
                GeodeticOrigin: new GeodeticOrigin(35.0, 139.0, 0.0));

            return Task.FromResult<IImportedSceneSource>(new StubImportedSceneSource(metadata));
        }
    }

    private sealed class StubImportedSceneSource(ImportedSceneMetadata metadata) : IImportedSceneSource
    {
        public ImportedSceneMetadata Metadata { get; } = metadata;

        public async IAsyncEnumerable<ImportedObjectUnit> ReadObjectUnitsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ImportedObjectUnit(
                "object-1.gml",
                "bldg",
                1,
                [
                    new ImportedCityObject(
                        "object-1",
                        "Object 1",
                        "bldg",
                        "53394525",
                        1,
                        new Transform3D(new Float3(0, 0, 0)),
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
                        ]),
                ]);

            await Task.CompletedTask;
        }
    }

}
