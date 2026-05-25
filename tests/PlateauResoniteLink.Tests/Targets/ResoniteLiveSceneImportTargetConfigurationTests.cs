using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Targets.Resonite.Diagnostics;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class ResoniteLiveSceneImportTargetConfigurationTests
{
    private static BundledDefaultMaterialAssetStore CreateBundledDefaultMaterialAssetStore() => new();

    [Fact]
    public async Task OptionsConstructorEnablesMeshBakeByDefault()
    {
        await using ResoniteLiveSceneImportTarget importTarget = CreateImportTarget();

        Assert.True(importTarget.MeshBakeEnabled);
    }

    [Fact]
    public async Task OptionsConstructorCanDisableMeshBake()
    {
        await using ResoniteLiveSceneImportTarget importTarget = CreateImportTarget(enableMeshBake: false);

        Assert.False(importTarget.MeshBakeEnabled);
    }

    [Fact]
    public async Task OptionsConstructorUsesLargeMemoryProfileByDefault()
    {
        await using ResoniteLiveSceneImportTarget importTarget = CreateImportTarget();

        Assert.Equal(ResoniteImportMemoryProfile.Large, importTarget.MemoryProfile);
    }

    [Fact]
    public async Task OptionsConstructorReusesDependencyDiagnostics()
    {
        ResoniteLinkSendDiagnostics diagnostics = ResoniteLinkSendDiagnostics.CreateEnabled();
        ResoniteMaterialPlanning materialPlanning = new(CreateBundledDefaultMaterialAssetStore());
        await using ResoniteLiveSceneImportTarget importTarget = new(
            new ResoniteLiveSceneImportTargetOptions(
                new Uri("ws://localhost:12345/"),
                1,
                EnableSendMetrics: true,
                ResoniteImportMemoryProfile.Large,
                EnableMeshBake: true,
                TerrainTileCacheRoot: null,
                DisableTerrainTileCache: false,
                ProgressReporter: null),
            ResoniteLiveSceneImportTargetTestSupport.CreateDependencies(
                new DelegatingClientSession(),
                diagnostics,
                ResoniteLiveSceneImportTargetTestSupport.CreateRunStarter(materialPlanning)));

        Assert.Same(diagnostics, importTarget.Diagnostics);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task FactoryCreateReusesTransportDiagnostics()
    {
        ServiceProvider provider = new ServiceCollection()
            .AddResoniteLiveSendTargetServices()
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        using HttpClient terrainTextureAssetHttpClient = new();
        ISceneSink target = scope.ServiceProvider
            .GetRequiredService<IResoniteLiveSceneImportFactory>()
            .CreateTarget(
                new ResoniteLiveSceneImportTargetOptions(
                    new Uri("ws://localhost:12345/"),
                    1,
                    EnableSendMetrics: true,
                    MemoryProfile: ResoniteImportMemoryProfile.Large,
                    EnableMeshBake: true,
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    ProgressReporter: null),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget importTarget = Assert.IsType<ResoniteLiveSceneImportTarget>(target);

        Assert.Same(importTarget.Diagnostics, importTarget.ClientSession.Diagnostics);
    }

    [Fact]
    public void AddResoniteLiveSendTargetServicesRegistersRunSetupPreparer()
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddResoniteLiveSendTargetServices()
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Assert.IsType<ResoniteLiveSendRunSetupPreparer>(
            scope.ServiceProvider.GetRequiredService<IResoniteLiveSendRunSetupPreparer>());
    }

    [Fact]
    public void AddResoniteLiveSendTargetServicesRegistersConnectionInitializer()
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddResoniteLiveSendTargetServices()
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Assert.IsType<ResoniteLiveSendConnectionInitializer>(
            scope.ServiceProvider.GetRequiredService<IResoniteLiveSendConnectionInitializer>());
    }

    [Fact]
    public void AddResoniteLiveSendTargetServicesRegistersPreparedRunSetupComposer()
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddResoniteLiveSendTargetServices()
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Assert.IsType<ResonitePreparedRunSetupComposer>(
            scope.ServiceProvider.GetRequiredService<IResonitePreparedRunSetupComposer>());
    }

    [Fact]
    public void AddResoniteLiveSendTargetServicesRegistersRunRuntimeComponentsFactory()
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddResoniteLiveSendTargetServices()
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Assert.IsType<LiveSendRunRuntimeComponentsFactory>(
            scope.ServiceProvider.GetRequiredService<ILiveSendRunRuntimeComponentsFactory>());
    }

    [Fact]
    public void AddResoniteLiveSendTargetServicesRegistersPhaseContextFactory()
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddResoniteLiveSendTargetServices()
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Assert.IsType<ResoniteLiveSendPhaseContextFactory>(
            scope.ServiceProvider.GetRequiredService<IResoniteLiveSendPhaseContextFactory>());
    }

    [Fact]
    public void AddResoniteLiveSendTargetServicesRegistersWorkerPipelineFactory()
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddResoniteLiveSendTargetServices()
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Assert.IsType<ResoniteLiveSendWorkerPipelineFactory>(
            scope.ServiceProvider.GetRequiredService<IResoniteLiveSendWorkerPipelineFactory>());
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredSessionFactory()
    {
        ILiveSendClientSession? recordedSession = null;
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteClientSessionFactory>(
                _ => new RecordingClientSessionFactory(() => recordedSession = new DelegatingClientSession()))
            .AddResoniteLiveSendTargetServices()
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        using HttpClient terrainTextureAssetHttpClient = new();
        ISceneSink target = scope.ServiceProvider
            .GetRequiredService<IResoniteLiveSceneImportFactory>()
            .CreateTarget(
                new ResoniteLiveSceneImportTargetOptions(
                    new Uri("ws://localhost:12345/"),
                    1,
                    EnableSendMetrics: false,
                    MemoryProfile: ResoniteImportMemoryProfile.Large,
                    EnableMeshBake: true,
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    ProgressReporter: null),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget importTarget = Assert.IsType<ResoniteLiveSceneImportTarget>(target);

        Assert.NotNull(recordedSession);
        Assert.Same(recordedSession, importTarget.ClientSession);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredTerrainTextureFactory()
    {
        RecordingTerrainTextureAssetGeneratorFactory terrainTextureFactory = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<ITerrainTextureAssetGeneratorFactory>(_ => terrainTextureFactory)
            .AddResoniteLiveSendTargetServices()
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        using HttpClient terrainTextureAssetHttpClient = new();
        ISceneSink target = scope.ServiceProvider
            .GetRequiredService<IResoniteLiveSceneImportFactory>()
            .CreateTarget(
                new ResoniteLiveSceneImportTargetOptions(
                    new Uri("ws://localhost:12345/"),
                    1,
                    EnableSendMetrics: false,
                    MemoryProfile: ResoniteImportMemoryProfile.Large,
                    EnableMeshBake: true,
                    TerrainTileCacheRoot: "cache-root",
                    DisableTerrainTileCache: true,
                    ProgressReporter: null),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget _ = Assert.IsType<ResoniteLiveSceneImportTarget>(target);

        Assert.Equal(1, terrainTextureFactory.CreateCallCount);
        Assert.Same(terrainTextureAssetHttpClient, terrainTextureFactory.LastHttpClient);
        Assert.NotNull(terrainTextureFactory.LastOptions);
        Assert.Equal("cache-root", terrainTextureFactory.LastOptions!.TerrainTileCacheRoot);
        Assert.True(terrainTextureFactory.LastOptions.DisableTerrainTileCache);
    }

    [Fact]
    public async Task AddResoniteLiveSendTargetServicesRoutesCanonicalDumpThroughRegisteredLiveSceneImportFactory()
    {
        RecordingLiveSceneImportFactory importFactory = new(new ResoniteMaterialPlanning(CreateBundledDefaultMaterialAssetStore()));
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteLiveSceneImportFactory>(_ => importFactory)
            .AddResoniteLiveSendTargetServices()
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        using TemporaryDirectory outputDirectory = new();
        string outputPath = Path.Combine(outputDirectory.Path, "scene.json");

        await using ISceneSink _ = scope.ServiceProvider
            .GetRequiredService<IResoniteCanonicalSceneDumpSinkFactory>()
            .Create(
                new ResoniteLiveSceneImportTargetOptions(
                    new Uri("ws://localhost:12345/"),
                    1,
                    EnableSendMetrics: true,
                    MemoryProfile: ResoniteImportMemoryProfile.Large,
                    EnableMeshBake: true,
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    ProgressReporter: null),
                outputPath);

        Assert.Equal(1, importFactory.PreconfiguredCreateCallCount);
        Assert.NotNull(importFactory.LastClientSession);
        Assert.Same(ResoniteLinkSendDiagnostics.Disabled, importFactory.LastDiagnostics);
        Assert.NotNull(importFactory.LastTerrainTextureAssetGenerator);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredWorkerPipelineFactory()
    {
        RecordingTerrainTextureAssetGeneratorFactory terrainTextureFactory = new();
        RecordingWorkerPipelineFactory workerPipelineFactory = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<ITerrainTextureAssetGeneratorFactory>(_ => terrainTextureFactory)
            .AddScoped<IResoniteLiveSendWorkerPipelineFactory>(_ => workerPipelineFactory)
            .AddResoniteLiveSendTargetServices()
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        using HttpClient terrainTextureAssetHttpClient = new();
        ISceneSink target = scope.ServiceProvider
            .GetRequiredService<IResoniteLiveSceneImportFactory>()
            .CreateTarget(
                new ResoniteLiveSceneImportTargetOptions(
                    new Uri("ws://localhost:12345/"),
                    1,
                    EnableSendMetrics: false,
                    MemoryProfile: ResoniteImportMemoryProfile.Large,
                    EnableMeshBake: true,
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    ProgressReporter: null),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget _ = Assert.IsType<ResoniteLiveSceneImportTarget>(target);

        Assert.Equal(1, workerPipelineFactory.CreateCallCount);
        Assert.NotNull(terrainTextureFactory.LastGenerator);
        Assert.Same(terrainTextureFactory.LastGenerator, workerPipelineFactory.LastTerrainTextureAssetGenerator);
    }

    [Fact]
    public void AddResoniteLiveSendTargetServicesPreservesPreRegisteredNonDemSourceFileBakeEmitterFactory()
    {
        RecordingNonDemSourceFileBakeEmitterFactory sourceFileBakeEmitterFactory = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<INonDemSourceFileBakeEmitterFactory>(_ => sourceFileBakeEmitterFactory)
            .AddResoniteLiveSendTargetServices()
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        CompositeCityObjectBaker? baker = scope.ServiceProvider
            .GetRequiredService<IResoniteBufferedCityObjectBakerFactory>()
            .Create(
                enableMeshBake: true,
                ResoniteImportBudgetProfiles.ForProfile(ResoniteImportMemoryProfile.Small));

        Assert.NotNull(baker);
        Assert.Equal(1, sourceFileBakeEmitterFactory.CreateCallCount);
        ResoniteImportBudgetProfile? resourceBudget = sourceFileBakeEmitterFactory.LastBudget.ResourceBudget;
        Assert.NotNull(resourceBudget);
        Assert.Equal(ResoniteImportMemoryProfile.Small, resourceBudget.Name);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesRegistersDefaultBaseClientFactory()
    {
        ServiceProvider provider = new ServiceCollection()
            .AddResoniteLiveSendTargetServices()
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        using HttpClient terrainTextureAssetHttpClient = new();
        ISceneSink target = scope.ServiceProvider
            .GetRequiredService<IResoniteLiveSceneImportFactory>()
            .CreateTarget(
                new ResoniteLiveSceneImportTargetOptions(
                    new Uri("ws://localhost:12345/"),
                    1,
                    EnableSendMetrics: false,
                    MemoryProfile: ResoniteImportMemoryProfile.Large,
                    EnableMeshBake: true,
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    ProgressReporter: null),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget importTarget = Assert.IsType<ResoniteLiveSceneImportTarget>(target);

        Assert.NotNull(importTarget.ClientSession);
    }

    [Theory]
    [InlineData(ResoniteImportMemoryProfile.Small)]
    [InlineData(ResoniteImportMemoryProfile.Large)]
    public async Task BufferedCityObjectBakerFactoryBuffersLod1NonDemObjectsAcrossMemoryProfiles(
        ResoniteImportMemoryProfile memoryProfile)
    {
        Assert.Equal(
            0,
            await CountReadyBeforeFlushAsync(
                memoryProfile,
                1,
                _ => CreateTriangleBuilding(
                    "tran-lod1",
                    x: 10.0,
                    z: 10.0,
                    sourceUnitKey: "shared-unit",
                    sourceFileRelativePath: "shared-unit.gml") with
                {
                    PackageName = "tran",
                }));
    }

    [Theory]
    [InlineData(ResoniteImportMemoryProfile.Small)]
    [InlineData(ResoniteImportMemoryProfile.Large)]
    public async Task BufferedCityObjectBakerFactoryKeepsMeshesAboveUInt16VertexRangeBufferedUntilExplicitFlush(
        ResoniteImportMemoryProfile memoryProfile)
    {
        Assert.Equal(
            0,
            await CountReadyBeforeFlushAsync(
                memoryProfile,
                1,
                index => CreateDenseTriangleBuilding(
                    $"dense-{index}",
                    65_536,
                    x: 10.0 + (index * 0.01),
                    z: 10.0,
                    sourceUnitKey: "shared-unit",
                    sourceFileRelativePath: "shared-unit.gml")));
    }

    [Theory]
    [InlineData(ResoniteImportMemoryProfile.Small)]
    [InlineData(ResoniteImportMemoryProfile.Large)]
    public async Task BufferedCityObjectBakerFactorySkipsDemObjectsAcrossMemoryProfiles(
        ResoniteImportMemoryProfile memoryProfile)
    {
        Assert.Equal(
            1,
            await CountReadyBeforeFlushAsync(
                memoryProfile,
                1,
                _ => CreateTriangleBuilding(
                    "dem-lod1",
                    x: 10.0,
                    z: 10.0,
                    sourceUnitKey: "shared-unit",
                    sourceFileRelativePath: null) with
                {
                    PackageName = "dem",
                    LodLevel = null,
                }));
    }


    private static ResoniteLiveSceneImportTarget CreateImportTarget(bool enableMeshBake = true)
    {
        ResoniteLinkSendDiagnostics diagnostics = ResoniteLinkSendDiagnostics.Disabled;
        ResoniteMaterialPlanning materialPlanning = new(CreateBundledDefaultMaterialAssetStore());
        return new ResoniteLiveSceneImportTarget(
            new ResoniteLiveSceneImportTargetOptions(
                new Uri("ws://localhost:12345/"),
                1,
                EnableSendMetrics: false,
                ResoniteImportMemoryProfile.Large,
                enableMeshBake,
                TerrainTileCacheRoot: null,
                DisableTerrainTileCache: false,
                ProgressReporter: null),
            ResoniteLiveSceneImportTargetTestSupport.CreateDependencies(
                new DelegatingClientSession(),
                diagnostics,
                ResoniteLiveSceneImportTargetTestSupport.CreateRunStarter(materialPlanning)));
    }

    private sealed class RecordingTerrainTextureAssetGeneratorFactory : ITerrainTextureAssetGeneratorFactory
    {
        public int CreateCallCount { get; private set; }

        public HttpClient? LastHttpClient { get; private set; }

        public ResoniteLiveSceneImportTargetOptions? LastOptions { get; private set; }

        public ITerrainTextureAssetGenerator? LastGenerator { get; private set; }

        public ITerrainTextureAssetGenerator Create(
            HttpClient terrainTextureAssetHttpClient,
            ResoniteLiveSceneImportTargetOptions options)
        {
            CreateCallCount++;
            LastHttpClient = terrainTextureAssetHttpClient;
            LastOptions = options;
            LastGenerator = new RecordingTerrainTextureAssetGenerator();
            return LastGenerator;
        }
    }

    private sealed class RecordingTerrainTextureAssetGenerator : ITerrainTextureAssetGenerator
    {
        public Task<GeneratedTerrainTexture> EnsureTextureAsync(
            TerrainTextureOverlay terrainTextureOverlay,
            CancellationToken cancellationToken)
        {
            _ = terrainTextureOverlay;
            cancellationToken.ThrowIfCancellationRequested();
            throw new NotSupportedException("This test only verifies DI override preservation during target creation.");
        }
    }

    private sealed class RecordingLiveSceneImportFactory(
        IResoniteMaterialPlanning materialPlanning) : IResoniteLiveSceneImportFactory
    {
        public int PreconfiguredCreateCallCount { get; private set; }

        public ILiveSendClientSession? LastClientSession { get; private set; }

        public ResoniteLinkSendDiagnostics? LastDiagnostics { get; private set; }

        public ITerrainTextureAssetGenerator? LastTerrainTextureAssetGenerator { get; private set; }

        public ResoniteLiveSceneImportTarget CreateTarget(
            ResoniteLiveSceneImportTargetOptions options,
            HttpClient terrainTextureAssetHttpClient)
        {
            _ = terrainTextureAssetHttpClient;
            return CreateTarget(
                options,
                new DelegatingClientSession(),
                ResoniteLinkSendDiagnostics.Disabled,
                new RecordingTerrainTextureAssetGenerator());
        }

        public ResoniteLiveSceneImportTarget CreateTarget(
            ResoniteLiveSceneImportTargetOptions options,
            ILiveSendClientSession clientSession,
            ResoniteLinkSendDiagnostics diagnostics,
            ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
        {
            PreconfiguredCreateCallCount++;
            LastClientSession = clientSession;
            LastDiagnostics = diagnostics;
            LastTerrainTextureAssetGenerator = terrainTextureAssetGenerator;
            return new ResoniteLiveSceneImportTarget(
                options,
                ResoniteLiveSceneImportTargetTestSupport.CreateDependencies(
                    clientSession,
                    diagnostics,
                    ResoniteLiveSceneImportTargetTestSupport.CreateRunStarter(
                        materialPlanning,
                        terrainTextureAssetGenerator: terrainTextureAssetGenerator)));
        }
    }

    private sealed class RecordingNonDemSourceFileBakeEmitterFactory : INonDemSourceFileBakeEmitterFactory
    {
        public int CreateCallCount { get; private set; }

        public NonDemAtlasBakeBudget LastBudget { get; private set; }

        public INonDemSourceFileBakeEmitter Create(NonDemAtlasBakeBudget atlasBudget)
        {
            CreateCallCount++;
            LastBudget = atlasBudget;
            return new RecordingNonDemSourceFileBakeEmitter();
        }
    }

    private sealed class RecordingNonDemSourceFileBakeEmitter : INonDemSourceFileBakeEmitter
    {
        public Task<int> EmitAsync(
            NonDemSourceFileBatchKey sourceFileKey,
            IReadOnlyList<NonDemBufferedCityObject> cityObjects,
            int batchStartIndex,
            Func<ResoniteConstructionCityObject, CancellationToken, Task> onBakedCityObject,
            CancellationToken cancellationToken)
        {
            _ = sourceFileKey;
            _ = cityObjects;
            _ = batchStartIndex;
            _ = onBakedCityObject;
            cancellationToken.ThrowIfCancellationRequested();
            throw new NotSupportedException("This test only verifies DI override preservation during baker creation.");
        }
    }

    private sealed class RecordingWorkerPipelineFactory : IResoniteLiveSendWorkerPipelineFactory
    {
        public int CreateCallCount { get; private set; }

        public ITerrainTextureAssetGenerator? LastTerrainTextureAssetGenerator { get; private set; }

        public IResoniteQueuedCityObjectWorker Create(ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
        {
            CreateCallCount++;
            LastTerrainTextureAssetGenerator = terrainTextureAssetGenerator;
            return new RecordingQueuedCityObjectWorker();
        }
    }

    private sealed class RecordingQueuedCityObjectWorker : IResoniteQueuedCityObjectWorker
    {
        public Task[] CreateProcessingTasks(
            LiveSendRunState state,
            LiveSendWorkerContext context)
        {
            _ = state;
            _ = context;
            return [];
        }
    }

    private sealed class RecordingClientSessionFactory(
        Func<ILiveSendClientSession> createSession)
        : IResoniteClientSessionFactory
    {
        public ILiveSendClientSession Create(
            ResoniteLiveSceneImportTargetOptions options,
            ResoniteLinkSendDiagnostics diagnostics)
        {
            _ = options;
            _ = diagnostics;
            return createSession();
        }
    }

    private static async Task<int> CountReadyBeforeFlushAsync(
        ResoniteImportMemoryProfile memoryProfile,
        int cityObjectCount,
        Func<int, ResoniteConstructionCityObject> createCityObject)
    {
        ResoniteBufferedCityObjectBakerFactory factory = new(
            new NonDemSourceFileBakeEmitterFactory(new ResoniteTextureImageLoader()));
        CompositeCityObjectBaker baker = factory.Create(
                enableMeshBake: true,
                ResoniteImportBudgetProfiles.ForProfile(memoryProfile))
            ?? throw new InvalidOperationException("Expected mesh bake composite baker.");

        int readyBeforeFlush = 0;
        for (int index = 0; index < cityObjectCount; index++)
        {
            IReadOnlyList<ResoniteConstructionCityObject> ready = await baker.BufferAsync(createCityObject(index));
            readyBeforeFlush += ready.Count;
        }

        return readyBeforeFlush;
    }

    private static ResoniteConstructionCityObject CreateTriangleBuilding(
        string slotKey,
        double x,
        double z,
        string sourceUnitKey = "unit",
        string? sourceFileRelativePath = "source.gml")
    {
        return CreateDenseTriangleBuilding(slotKey, 3, x, z, sourceUnitKey, sourceFileRelativePath);
    }

    private static ResoniteConstructionCityObject CreateDenseTriangleBuilding(
        string slotKey,
        int vertexCount,
        double x,
        double z,
        string sourceUnitKey = "unit",
        string? sourceFileRelativePath = "source.gml")
    {
        ResoniteMeshVertex[] vertices = Enumerable.Range(0, vertexCount)
            .Select(index => new ResoniteMeshVertex(
                new ResoniteFloat3(index, 0.0, 0.0),
                new ResoniteFloat3(0.0, 1.0, 0.0),
                new ResoniteFloat2(0.0, 0.0)))
            .ToArray();

        return new ResoniteConstructionCityObject(
            SlotKey: slotKey,
            DisplayName: slotKey,
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 1,
            Transform: new ResoniteTransform(new ResoniteFloat3(x, 0.0, z)),
            Mesh: new ResoniteImportedMesh(
                vertices,
                [
                    new ResoniteMeshSubmesh(0, [0, 1, 2]),
                ]),
            Materials:
            [
                new ResoniteMaterialBinding(new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    ResoniteMaterialType.Standard,
                    null,
                    ResoniteTextureSourceKind.Bundled,
                    ResoniteMaterialProjection.Uv,
                    null,
                    [0]),
            ],
            SourceFileRelativePath: sourceFileRelativePath);
    }
}
