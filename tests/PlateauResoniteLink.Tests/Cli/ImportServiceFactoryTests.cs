using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Application.Importing.Contracts;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
            CommonMaterialCatalog.Create(),
            archiveFileLayoutPolicy);

        PlateauImportRequest firstRequest = CreateRequest("53394525");
        PlateauImportRequest secondRequest = CreateRequest("53394526");
        ImportRunCliOptions runOptions = new("local");
        ImportSinkCliOptions firstSinkOptions = CreateSinkOptions();
        ImportSinkCliOptions secondSinkOptions = CreateSinkOptions();
        ResoniteSceneBuildCliOptions firstSceneBuildOptions = CreateSceneBuildOptions(enableMeshBake: true);
        ResoniteSceneBuildCliOptions secondSceneBuildOptions = CreateSceneBuildOptions(enableMeshBake: false);

        PlateauImportService firstService = factory.Create(firstSinkOptions, firstSceneBuildOptions);
        PlateauImportService secondService = factory.Create(secondSinkOptions, secondSceneBuildOptions);

        Assert.NotSame(firstService, secondService);
        Assert.Equal(2, datasetResolverFactory.CreatedResolvers.Count);
        Assert.Equal(2, sceneImportTargetFactory.CreatedTargets.Count);
        Assert.Equal(
            [(firstSinkOptions, firstSceneBuildOptions), (secondSinkOptions, secondSceneBuildOptions)],
            sceneImportTargetFactory.CapturedOptions);

        ImportExecutionResult firstResult = await firstService.ExecuteAsync(firstRequest, runOptions.WorkRoot);
        ImportExecutionResult secondResult = await secondService.ExecuteAsync(secondRequest, runOptions.WorkRoot);

        Assert.Equal("PLATEAU tokyo23ku 53394525", firstResult.Metadata.SceneName);
        Assert.Equal("PLATEAU tokyo23ku 53394526", secondResult.Metadata.SceneName);
        Assert.True(sceneImportTargetFactory.CreatedTargets.All(static target => target.DisposeCallCount == 1));
        Assert.Equal(2, importedSceneSourceFactory.CreateCallCount);
    }

    private static PlateauImportRequest CreateRequest(string meshCode)
    {
        return new PlateauImportRequest(
            Dataset: "tokyo23ku",
            MeshCode: meshCode,
            CityGmlSource: DatasetLocation.Local(TestData.GetFixturePath("LocalPlateauDataset")));
    }

    private static ImportSinkCliOptions CreateSinkOptions()
    {
        return new LiveResoniteSinkCliOptions(
            new ResoniteLiveTransportCliOptions(new Uri("ws://localhost:12345/"), 4, enableSendMetrics: false),
            new TerrainTileCacheCliOptions(TerrainTileCacheRoot: null, DisableTerrainTileCache: false));
    }

    private static ResoniteSceneBuildCliOptions CreateSceneBuildOptions(bool enableMeshBake)
    {
        return new ResoniteSceneBuildCliOptions(PlateauImportMemoryProfile.Large, enableMeshBake);
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
        public Task<ResolvedLocalPlateauImportRequest> ResolveAsync(
            ValidatedPlateauImportRequest request,
            string workRoot,
            CancellationToken cancellationToken = default)
        {
            ValidatedLocalDatasetLocation localSource = Assert.IsType<ValidatedLocalDatasetLocation>(request.CityGmlSource);
            ValidatedLocalDatasetLocation? localDemTextureSource = request.DemTextureSource is null
                ? null
                : Assert.IsType<ValidatedLocalDatasetLocation>(request.DemTextureSource);
            return Task.FromResult(ResolvedLocalPlateauImportRequest.Create(request, localSource, localDemTextureSource, workRoot));
        }
    }

    private sealed class StubSceneSinkFactory : ISceneSinkFactory
    {
        public List<(ImportSinkCliOptions Sink, ResoniteSceneBuildCliOptions SceneBuild)> CapturedOptions { get; } = [];
        public List<StubSceneImportSink> CreatedTargets { get; } = [];

        public ISceneSink Create(
            ImportSinkCliOptions sinkOptions,
            ResoniteSceneBuildCliOptions sceneBuildOptions)
        {
            CapturedOptions.Add((sinkOptions, sceneBuildOptions));
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
            ResolvedLocalPlateauImportRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            CreateCallCount++;

            ImportedSceneMetadata metadata = new(
                "3.0",
                $"PLATEAU {request.Dataset} {request.MeshCode}",
                request.ToImportRequest(),
                new PlateauSourceDataset(["bldg"], [], []),
                new Attribution(
                    new LicenseMetadata(true, "credit", "license", "https://example.invalid")),
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
                                new MeshSubmesh(0, [0, 1, 2]),
                            ])),
                        [
                            new MaterialBinding(new ColorRgba(1, 1, 1, 1),
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
