using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;
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
            new ResoniteLiveSceneImportSession(
                new DelegatingClientSession(),
                diagnostics),
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionServices());

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
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredTextureImageLoaderFactory()
    {
        RecordingTextureImageLoaderFactory textureImageLoaderFactory = new();
        using SceneSinkRecordingClient routedClient = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteClientSessionFactory>(
                _ => new RecordingClientSessionFactory(() => new DelegatingClientSession(routedClient)))
            .AddScoped<IResoniteTextureImageLoaderFactory>(_ => textureImageLoaderFactory)
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
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            "tokyo23ku",
            "53394525",
            datasetDirectory.Path,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            sourceFiles: ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            []);

        Assert.Equal(1, textureImageLoaderFactory.CreateCallCount);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredCommonMaterialSetupAssetPreparer()
    {
        RecordingCommonMaterialSetupAssetPreparer commonMaterialPreparer = new();
        using SceneSinkRecordingClient routedClient = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteClientSessionFactory>(
                _ => new RecordingClientSessionFactory(() => new DelegatingClientSession(routedClient)))
            .AddScoped<IResoniteCommonMaterialSetupAssetPreparer>(_ => commonMaterialPreparer)
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
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            "tokyo23ku",
            "53394525",
            datasetDirectory.Path,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            sourceFiles: ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            []);

        Assert.Equal(1, commonMaterialPreparer.PrepareCallCount);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredSceneSetupRunner()
    {
        RecordingSceneSetupRunner sceneSetupRunner = new();
        using SceneSinkRecordingClient routedClient = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteClientSessionFactory>(
                _ => new RecordingClientSessionFactory(() => new DelegatingClientSession(routedClient)))
            .AddScoped<IResoniteLiveSendSceneSetupRunner>(_ => sceneSetupRunner)
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
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            "tokyo23ku",
            "53394525",
            datasetDirectory.Path,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            sourceFiles: ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            []);

        Assert.Equal(1, sceneSetupRunner.SetupCallCount);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredRunPlanFactory()
    {
        RecordingRunPlanFactory runPlanFactory = new();
        using SceneSinkRecordingClient routedClient = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteClientSessionFactory>(
                _ => new RecordingClientSessionFactory(() => new DelegatingClientSession(routedClient)))
            .AddScoped<ILiveSendRunPlanFactory>(_ => runPlanFactory)
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
                    MemoryProfile: ResoniteImportMemoryProfile.Small,
                    EnableMeshBake: false,
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    ProgressReporter: null),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget importTarget = Assert.IsType<ResoniteLiveSceneImportTarget>(target);
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            "tokyo23ku",
            "53394525",
            datasetDirectory.Path,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            sourceFiles: ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            []);

        Assert.Equal(1, runPlanFactory.CreateCallCount);
        Assert.Equal(ResoniteImportMemoryProfile.Small, runPlanFactory.LastMemoryProfile);
        Assert.False(runPlanFactory.LastMeshBakeEnabled);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredSharedSlotIndexFactory()
    {
        RecordingSharedSlotIndexFactory sharedSlotIndexFactory = new();
        using SceneSinkRecordingClient routedClient = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteClientSessionFactory>(
                _ => new RecordingClientSessionFactory(() => new DelegatingClientSession(routedClient)))
            .AddScoped<IResoniteSharedSlotIndexFactory>(_ => sharedSlotIndexFactory)
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
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            "tokyo23ku",
            "53394525",
            datasetDirectory.Path,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            sourceFiles: ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            []);

        Assert.Equal(1, sharedSlotIndexFactory.CreateCallCount);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredRunStateFactory()
    {
        RecordingRunStateFactory runStateFactory = new();
        using SceneSinkRecordingClient routedClient = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteClientSessionFactory>(
                _ => new RecordingClientSessionFactory(() => new DelegatingClientSession(routedClient)))
            .AddScoped<ILiveSendRunStateFactory>(_ => runStateFactory)
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
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            "tokyo23ku",
            "53394525",
            datasetDirectory.Path,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            sourceFiles: ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            []);

        Assert.Equal(1, runStateFactory.CreateCallCount);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredPreparedGeometryFactory()
    {
        RecordingPreparedGeometryFactory preparedGeometryFactory = new();
        using SceneSinkRecordingClient routedClient = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteClientSessionFactory>(
                _ => new RecordingClientSessionFactory(() => new DelegatingClientSession(routedClient)))
            .AddScoped<IResonitePreparedGeometryFactory>(_ => preparedGeometryFactory)
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
                    EnableMeshBake: false,
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    ProgressReporter: null),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget importTarget = Assert.IsType<ResoniteLiveSceneImportTarget>(target);
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            "tokyo23ku",
            "53394525",
            datasetDirectory.Path,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            sourceFiles: ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            [CreateValidTriangleBuilding("prepared-geometry-override", sourceFileRelativePath: "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml")]);

        Assert.Equal(1, preparedGeometryFactory.ValidateCallCount);
        Assert.Equal(1, preparedGeometryFactory.CreateCallCount);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredPreparedCityObjectFactoryFactory()
    {
        RecordingPreparedCityObjectFactoryFactory preparedCityObjectFactoryFactory = new();
        using SceneSinkRecordingClient routedClient = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteClientSessionFactory>(
                _ => new RecordingClientSessionFactory(() => new DelegatingClientSession(routedClient)))
            .AddScoped<IResonitePreparedCityObjectFactoryFactory>(_ => preparedCityObjectFactoryFactory)
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
                    EnableMeshBake: false,
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    ProgressReporter: null),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget importTarget = Assert.IsType<ResoniteLiveSceneImportTarget>(target);
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            "tokyo23ku",
            "53394525",
            datasetDirectory.Path,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            sourceFiles: ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            [CreateValidTriangleBuilding("prepared-city-object-factory-factory-override", sourceFileRelativePath: "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml")]);

        Assert.Equal(1, preparedCityObjectFactoryFactory.CreateCallCount);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredGeometryAssetPlanner()
    {
        RecordingGeometryAssetPlanner geometryAssetPlanner = new();
        using SceneSinkRecordingClient routedClient = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteClientSessionFactory>(
                _ => new RecordingClientSessionFactory(() => new DelegatingClientSession(routedClient)))
            .AddScoped<IResoniteGeometryAssetPlanner>(_ => geometryAssetPlanner)
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
                    EnableMeshBake: false,
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    ProgressReporter: null),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget importTarget = Assert.IsType<ResoniteLiveSceneImportTarget>(target);
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            "tokyo23ku",
            "53394525",
            datasetDirectory.Path,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            sourceFiles: ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            [CreateValidTriangleBuilding("geometry-planner-override", sourceFileRelativePath: "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml")]);

        Assert.Equal(1, geometryAssetPlanner.PlanCallCount);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredSceneMaterialPlanFactory()
    {
        RecordingSceneMaterialPlanFactory sceneMaterialPlanFactory = new();
        using SceneSinkRecordingClient routedClient = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteClientSessionFactory>(
                _ => new RecordingClientSessionFactory(() => new DelegatingClientSession(routedClient)))
            .AddScoped<IResoniteSceneMaterialPlanFactory>(_ => sceneMaterialPlanFactory)
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
                    EnableMeshBake: false,
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    ProgressReporter: null),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget importTarget = Assert.IsType<ResoniteLiveSceneImportTarget>(target);
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            "tokyo23ku",
            "53394525",
            datasetDirectory.Path,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            sourceFiles: ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            [CreateValidTriangleBuilding("material-plan-factory-override", sourceFileRelativePath: "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml")]);

        Assert.Equal(1, sceneMaterialPlanFactory.CreateCallCount);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredPreparedCityObjectImporter()
    {
        RecordingPreparedCityObjectImporter preparedCityObjectImporter = new();
        using SceneSinkRecordingClient routedClient = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteClientSessionFactory>(
                _ => new RecordingClientSessionFactory(() => new DelegatingClientSession(routedClient)))
            .AddScoped<IResonitePreparedCityObjectImporter>(_ => preparedCityObjectImporter)
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
                    EnableMeshBake: false,
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    ProgressReporter: null),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget importTarget = Assert.IsType<ResoniteLiveSceneImportTarget>(target);
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            "tokyo23ku",
            "53394525",
            datasetDirectory.Path,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            sourceFiles: ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            [CreateValidTriangleBuilding("prepared-city-object-importer-override", sourceFileRelativePath: "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml")]);

        Assert.Equal(1, preparedCityObjectImporter.ImportCallCount);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredQueuedCityObjectSender()
    {
        RecordingQueuedCityObjectSender queuedCityObjectSender = new();
        using SceneSinkRecordingClient routedClient = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteClientSessionFactory>(
                _ => new RecordingClientSessionFactory(() => new DelegatingClientSession(routedClient)))
            .AddScoped<IResoniteQueuedCityObjectSender>(_ => queuedCityObjectSender)
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
                    EnableMeshBake: false,
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    ProgressReporter: null),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget importTarget = Assert.IsType<ResoniteLiveSceneImportTarget>(target);
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            "tokyo23ku",
            "53394525",
            datasetDirectory.Path,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            sourceFiles: ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            [CreateValidTriangleBuilding("queued-city-object-sender-override", sourceFileRelativePath: "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml")]);

        Assert.Equal(1, queuedCityObjectSender.SendCallCount);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredQueuedCityObjectSenderFactory()
    {
        RecordingQueuedCityObjectSender queuedCityObjectSender = new();
        RecordingQueuedCityObjectSenderFactory queuedCityObjectSenderFactory = new(queuedCityObjectSender);
        using SceneSinkRecordingClient routedClient = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteClientSessionFactory>(
                _ => new RecordingClientSessionFactory(() => new DelegatingClientSession(routedClient)))
            .AddScoped<IResoniteQueuedCityObjectSenderFactory>(_ => queuedCityObjectSenderFactory)
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
                    EnableMeshBake: false,
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    ProgressReporter: null),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget importTarget = Assert.IsType<ResoniteLiveSceneImportTarget>(target);
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            "tokyo23ku",
            "53394525",
            datasetDirectory.Path,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            sourceFiles: ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            [CreateValidTriangleBuilding("queued-city-object-sender-factory-override", sourceFileRelativePath: "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml")]);

        Assert.Equal(1, queuedCityObjectSenderFactory.CreateCallCount);
        Assert.Equal(1, queuedCityObjectSender.SendCallCount);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredRunFinalizer()
    {
        RecordingLiveSendRunFinalizer runFinalizer = new();
        using SceneSinkRecordingClient routedClient = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteClientSessionFactory>(
                _ => new RecordingClientSessionFactory(() => new DelegatingClientSession(routedClient)))
            .AddScoped<IResoniteLiveSendRunFinalizer>(_ => runFinalizer)
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
                    EnableMeshBake: false,
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    ProgressReporter: null),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget importTarget = Assert.IsType<ResoniteLiveSceneImportTarget>(target);
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            "tokyo23ku",
            "53394525",
            datasetDirectory.Path,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            sourceFiles: ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            [CreateValidTriangleBuilding("run-finalizer-override", sourceFileRelativePath: "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml")]);

        Assert.Equal(1, runFinalizer.CompleteCallCount);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredExecutionResultFactory()
    {
        RecordingLiveSendExecutionResultFactory executionResultFactory = new();
        using SceneSinkRecordingClient routedClient = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteClientSessionFactory>(
                _ => new RecordingClientSessionFactory(() => new DelegatingClientSession(routedClient)))
            .AddScoped<IResoniteLiveSendExecutionResultFactory>(_ => executionResultFactory)
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
                    EnableMeshBake: false,
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    ProgressReporter: null),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget importTarget = Assert.IsType<ResoniteLiveSceneImportTarget>(target);
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            "tokyo23ku",
            "53394525",
            datasetDirectory.Path,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            sourceFiles: ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            [CreateValidTriangleBuilding("execution-result-factory-override", sourceFileRelativePath: "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml")]);

        Assert.Equal(1, executionResultFactory.CreateCallCount);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredRunResourceReleaser()
    {
        RecordingLiveSendRunResourceReleaser runResourceReleaser = new();
        using SceneSinkRecordingClient routedClient = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteClientSessionFactory>(
                _ => new RecordingClientSessionFactory(() => new DelegatingClientSession(routedClient)))
            .AddScoped<IResoniteLiveSendRunResourceReleaser>(_ => runResourceReleaser)
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
                    EnableMeshBake: false,
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    ProgressReporter: null),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget importTarget = Assert.IsType<ResoniteLiveSceneImportTarget>(target);
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            "tokyo23ku",
            "53394525",
            datasetDirectory.Path,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            sourceFiles: ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            [CreateValidTriangleBuilding("run-resource-releaser-override", sourceFileRelativePath: "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml")]);

        Assert.Equal(1, runResourceReleaser.ReleaseCallCount);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredExecutionGateFactory()
    {
        RecordingLiveSendExecutionGateFactory executionGateFactory = new();
        using SceneSinkRecordingClient routedClient = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteClientSessionFactory>(
                _ => new RecordingClientSessionFactory(() => new DelegatingClientSession(routedClient)))
            .AddScoped<IResoniteLiveSendExecutionGateFactory>(_ => executionGateFactory)
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
                    EnableMeshBake: false,
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    ProgressReporter: null),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget importTarget = Assert.IsType<ResoniteLiveSceneImportTarget>(target);
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            "tokyo23ku",
            "53394525",
            datasetDirectory.Path,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            sourceFiles: ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            [CreateValidTriangleBuilding("execution-gate-factory-override", sourceFileRelativePath: "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml")]);

        Assert.Equal(1, executionGateFactory.CreateCallCount);
        Assert.Equal(1, executionGateFactory.Gate.EnterCallCount);
        Assert.Equal(1, executionGateFactory.Gate.ReleaseCallCount);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredRunStarter()
    {
        RecordingLiveSendRunStarter runStarter = new();
        using SceneSinkRecordingClient routedClient = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteClientSessionFactory>(
                _ => new RecordingClientSessionFactory(() => new DelegatingClientSession(routedClient)))
            .AddScoped<IResoniteLiveSendRunStarter>(_ => runStarter)
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
                    EnableMeshBake: false,
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    ProgressReporter: null),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget importTarget = Assert.IsType<ResoniteLiveSceneImportTarget>(target);
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            "tokyo23ku",
            "53394525",
            datasetDirectory.Path,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            sourceFiles: ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            [CreateValidTriangleBuilding("run-starter-override", sourceFileRelativePath: "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml")]);

        Assert.Equal(1, runStarter.StartCallCount);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredConnectionInitializer()
    {
        RecordingLiveSendConnectionInitializer connectionInitializer = new();
        using SceneSinkRecordingClient routedClient = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteClientSessionFactory>(
                _ => new RecordingClientSessionFactory(() => new DelegatingClientSession(routedClient)))
            .AddScoped<IResoniteLiveSendConnectionInitializer>(_ => connectionInitializer)
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
                    EnableMeshBake: false,
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    ProgressReporter: null),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget importTarget = Assert.IsType<ResoniteLiveSceneImportTarget>(target);
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            "tokyo23ku",
            "53394525",
            datasetDirectory.Path,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            sourceFiles: ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            [CreateValidTriangleBuilding("connection-initializer-override", sourceFileRelativePath: "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml")]);

        Assert.Equal(1, connectionInitializer.EnsureConnectedCallCount);
        Assert.Equal("tokyo23ku", connectionInitializer.LastSetupInfo?.Dataset);
        Assert.Equal("53394525", connectionInitializer.LastNormalizedRequest?.MeshCode);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredPreparedTextureUploader()
    {
        RecordingPreparedTextureUploader preparedTextureUploader = new();
        using SceneSinkRecordingClient routedClient = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteClientSessionFactory>(
                _ => new RecordingClientSessionFactory(() => new DelegatingClientSession(routedClient)))
            .AddScoped<IResonitePreparedTextureUploader>(_ => preparedTextureUploader)
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
                    EnableMeshBake: false,
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    ProgressReporter: null),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget importTarget = Assert.IsType<ResoniteLiveSceneImportTarget>(target);
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            "tokyo23ku",
            "53394525",
            datasetDirectory.Path,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            sourceFiles: ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            [CreateValidTriangleBuilding("texture-uploader-override", sourceFileRelativePath: "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml")]);

        Assert.Equal(1, preparedTextureUploader.UploadCallCount);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredCityObjectQueueWriter()
    {
        RecordingCityObjectQueueWriter cityObjectQueueWriter = new();
        using SceneSinkRecordingClient routedClient = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteClientSessionFactory>(
                _ => new RecordingClientSessionFactory(() => new DelegatingClientSession(routedClient)))
            .AddScoped<IResoniteCityObjectQueueWriter>(_ => cityObjectQueueWriter)
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
                    EnableMeshBake: false,
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    ProgressReporter: null),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget importTarget = Assert.IsType<ResoniteLiveSceneImportTarget>(target);
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            "tokyo23ku",
            "53394525",
            datasetDirectory.Path,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            sourceFiles: ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            [CreateValidTriangleBuilding("queue-writer-override", sourceFileRelativePath: "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml")]);

        Assert.Equal(1, cityObjectQueueWriter.QueueObjectUnitCallCount);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredObjectUnitStreamQueueWriter()
    {
        RecordingImportedObjectUnitStreamQueueWriter objectUnitStreamQueueWriter = new();
        using SceneSinkRecordingClient routedClient = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteClientSessionFactory>(
                _ => new RecordingClientSessionFactory(() => new DelegatingClientSession(routedClient)))
            .AddScoped<IResoniteImportedObjectUnitStreamQueueWriter>(_ => objectUnitStreamQueueWriter)
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
                    EnableMeshBake: false,
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    ProgressReporter: null),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget importTarget = Assert.IsType<ResoniteLiveSceneImportTarget>(target);
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            "tokyo23ku",
            "53394525",
            datasetDirectory.Path,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            sourceFiles: ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            [CreateValidTriangleBuilding("stream-queue-writer-override", sourceFileRelativePath: "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml")]);

        Assert.Equal(1, objectUnitStreamQueueWriter.QueueCallCount);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredCityObjectSendWorkerPool()
    {
        RecordingCityObjectSendWorkerPool cityObjectSendWorkerPool = new();
        using SceneSinkRecordingClient routedClient = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteClientSessionFactory>(
                _ => new RecordingClientSessionFactory(() => new DelegatingClientSession(routedClient)))
            .AddScoped<IResoniteCityObjectSendWorkerPool>(_ => cityObjectSendWorkerPool)
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
                    EnableMeshBake: false,
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    ProgressReporter: null),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget importTarget = Assert.IsType<ResoniteLiveSceneImportTarget>(target);
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            "tokyo23ku",
            "53394525",
            datasetDirectory.Path,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            sourceFiles: ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            [CreateValidTriangleBuilding("worker-pool-override", sourceFileRelativePath: "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml")]);

        Assert.Equal(1, cityObjectSendWorkerPool.CreateProcessingTasksCallCount);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredWorkerLauncher()
    {
        RecordingLiveSendWorkerLauncher workerLauncher = new();
        using SceneSinkRecordingClient routedClient = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteClientSessionFactory>(
                _ => new RecordingClientSessionFactory(() => new DelegatingClientSession(routedClient)))
            .AddScoped<IResoniteLiveSendWorkerLauncher>(_ => workerLauncher)
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
                    EnableMeshBake: false,
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    ProgressReporter: null),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget importTarget = Assert.IsType<ResoniteLiveSceneImportTarget>(target);
        using TemporaryDirectory datasetDirectory = new();
        using TemporaryDirectory workDirectory = new();
        ImportedSceneMetadata metadata = ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
            "tokyo23ku",
            "53394525",
            datasetDirectory.Path,
            new ResoniteLocalOrigin(35.0, 139.0, 0.0),
            sourceFiles: ["udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml"]);

        _ = await ResoniteLiveSceneImportTargetTestSupport.ExecuteSceneAsync(
            importTarget,
            metadata,
            workDirectory.Path,
            [CreateValidTriangleBuilding("worker-launcher-override", sourceFileRelativePath: "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml")]);

        Assert.Equal(1, workerLauncher.StartCallCount);
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
            new ResoniteLiveSceneImportSession(
                new DelegatingClientSession(),
                diagnostics),
            ResoniteLiveSceneImportTargetTestSupport.CreateExecutionServices());
    }

    private sealed class RecordingTerrainTextureAssetGeneratorFactory : ITerrainTextureAssetGeneratorFactory
    {
        public int CreateCallCount { get; private set; }

        public HttpClient? LastHttpClient { get; private set; }

        public ResoniteLiveSceneImportTargetOptions? LastOptions { get; private set; }

        public ITerrainTextureAssetGenerator Create(
            HttpClient terrainTextureAssetHttpClient,
            ResoniteLiveSceneImportTargetOptions options)
        {
            CreateCallCount++;
            LastHttpClient = terrainTextureAssetHttpClient;
            LastOptions = options;
            return new RecordingTerrainTextureAssetGenerator();
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

    private sealed class RecordingTextureImageLoaderFactory : IResoniteTextureImageLoaderFactory
    {
        public int CreateCallCount { get; private set; }

        public ResoniteTextureImageLoader Create()
        {
            CreateCallCount++;
            return new ResoniteTextureImageLoader();
        }
    }

    private sealed class RecordingCommonMaterialSetupAssetPreparer : IResoniteCommonMaterialSetupAssetPreparer
    {
        public int PrepareCallCount { get; private set; }

        public Task PrepareAsync(
            IResoniteLinkClient client,
            ResoniteSceneSetupState setupState,
            CommonMaterialAssetCache materials,
            CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials,
            LiveSendProgressSink progress,
            Action<string>? reportProgress,
            CancellationToken cancellationToken)
        {
            _ = client;
            _ = setupState;
            _ = materials;
            _ = commonMaterials;
            _ = progress;
            _ = reportProgress;
            cancellationToken.ThrowIfCancellationRequested();
            PrepareCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSceneSetupRunner : IResoniteLiveSendSceneSetupRunner
    {
        private readonly ResoniteLiveSendSceneSetupRunner inner = new(
            new ResoniteSceneSetupInterpreter(new ResoniteSceneSlotLocator(), new ResoniteSceneAnchorResolver()),
            new ResoniteSharedSlotIndexFactory(),
            new ResoniteSlotCreator());

        public int SetupCallCount { get; private set; }

        public Task<ResoniteLiveSendSceneSetupResult> SetupAsync(
            IResoniteLinkClient routedClient,
            LiveSendRunPlan runPlan,
            CommonMaterialCatalog<DefaultCommonMaterialMember> commonMaterials,
            Action<string>? progressReporter,
            CancellationToken cancellationToken)
        {
            SetupCallCount++;
            return inner.SetupAsync(
                routedClient,
                runPlan,
                commonMaterials,
                progressReporter,
                cancellationToken);
        }
    }

    private sealed class RecordingRunPlanFactory : ILiveSendRunPlanFactory
    {
        private readonly LiveSendRunPlanFactory inner = new();

        public int CreateCallCount { get; private set; }

        public ResoniteImportMemoryProfile LastMemoryProfile { get; private set; }

        public bool LastMeshBakeEnabled { get; private set; }

        public LiveSendRunPlan Create(
            ResoniteSceneSetupInfo setupInfo,
            string resolvedWorkRoot,
            ResoniteLocalOrigin requestLocalOrigin,
            ResoniteImportMemoryProfile memoryProfile,
            int connectionCount,
            bool meshBakeEnabled)
        {
            CreateCallCount++;
            LastMemoryProfile = memoryProfile;
            LastMeshBakeEnabled = meshBakeEnabled;
            return inner.Create(
                setupInfo,
                resolvedWorkRoot,
                requestLocalOrigin,
                memoryProfile,
                connectionCount,
                meshBakeEnabled);
        }
    }

    private sealed class RecordingSharedSlotIndexFactory : IResoniteSharedSlotIndexFactory
    {
        private readonly ResoniteSharedSlotIndexFactory inner = new();

        public int CreateCallCount { get; private set; }

        public ResoniteSharedSlotIndex Create(
            ResoniteSceneSetupState setupState,
            ResoniteLocalOrigin requestLocalOrigin,
            IReadOnlyDictionary<string, string> sourceFileSlotNamesByRelativePath,
            Func<IResoniteLinkClient, ResoniteSlotLocator, string, ResoniteFloat3?, ResoniteFloatQ?, CancellationToken, Task<CreatedSlot>> createSlotAsync)
        {
            CreateCallCount++;
            return inner.Create(
                setupState,
                requestLocalOrigin,
                sourceFileSlotNamesByRelativePath,
                createSlotAsync);
        }
    }

    private sealed class RecordingRunStateFactory : ILiveSendRunStateFactory
    {
        private readonly LiveSendRunStateFactory inner = new(new ResoniteBufferedCityObjectBakerFactory());

        public int CreateCallCount { get; private set; }

        public LiveSendRunState Create(
            LiveSendRunPlan runPlan,
            ResoniteSceneSetupState setupState,
            LiveSendProgressSink progress,
            CommonMaterialAssetCache materials,
            ResoniteSharedSlotIndex placement,
            ResoniteTextureImageLoader textureImageLoader,
            CancellationToken cancellationToken)
        {
            CreateCallCount++;
            return inner.Create(
                runPlan,
                setupState,
                progress,
                materials,
                placement,
                textureImageLoader,
                cancellationToken);
        }
    }

    private sealed class RecordingPreparedGeometryFactory : IResonitePreparedGeometryFactory
    {
        private readonly ResonitePreparedGeometryFactory inner = new();

        public int ValidateCallCount { get; private set; }

        public int CreateCallCount { get; private set; }

        public void ValidateForPreparation(ResoniteConstructionCityObject cityObject)
        {
            ValidateCallCount++;
            inner.ValidateForPreparation(cityObject);
        }

        public Task<PreparedConstructionGeometry> CreateAsync(
            ResoniteConstructionCityObject cityObject,
            CancellationToken cancellationToken)
        {
            CreateCallCount++;
            return inner.CreateAsync(cityObject, cancellationToken);
        }

        public ResoniteConstructionCityObject ApplyTerrainTextureCanvasUv(
            ResoniteConstructionCityObject cityObject,
            Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay)
        {
            return inner.ApplyTerrainTextureCanvasUv(cityObject, preparedTerrainTextureDataByOverlay);
        }

        public PreparedConstructionGeometry RecreateStaticMeshIfNeeded(
            ResoniteConstructionCityObject cityObject,
            PreparedConstructionGeometry preparedGeometry)
        {
            return inner.RecreateStaticMeshIfNeeded(cityObject, preparedGeometry);
        }
    }

    private sealed class RecordingPreparedCityObjectFactoryFactory : IResonitePreparedCityObjectFactoryFactory
    {
        public int CreateCallCount { get; private set; }

        public IResonitePreparedCityObjectFactory Create(ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
        {
            CreateCallCount++;
            return ResoniteLiveSceneImportTargetTestSupport.CreatePreparedCityObjectFactory(terrainTextureAssetGenerator);
        }
    }

    private sealed class RecordingGeometryAssetPlanner : IResoniteGeometryAssetPlanner
    {
        private readonly ResoniteGeometryAssetPlanner inner = new(new ResoniteGeometryAssetAssembler());

        public int PlanCallCount { get; private set; }

        public Task<PlannedGeometryAsset> PlanAsync(
            IResoniteLinkClient importClient,
            ResoniteConstructionCityObject cityObject,
            PreparedCityObject preparedCityObject,
            IReadOnlyDictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay,
            Action<string>? progressReporter,
            CancellationToken cancellationToken)
        {
            PlanCallCount++;
            return inner.PlanAsync(
                importClient,
                cityObject,
                preparedCityObject,
                preparedTerrainTextureDataByOverlay,
                progressReporter,
                cancellationToken);
        }
    }

    private sealed class RecordingPreparedTextureUploader : IResonitePreparedTextureUploader
    {
        private readonly ResonitePreparedTextureUploader inner =
            ResoniteLiveSceneImportTargetTestSupport.CreatePreparedTextureUploader();

        public int UploadCallCount { get; private set; }

        public Task<UploadedTextureAssetSet> UploadAsync(
            LiveSendRunState state,
            IResoniteLinkClient importClient,
            PreparedCityObject preparedCityObject,
            Dictionary<TerrainTextureOverlay, GeneratedTerrainTexture> preparedTerrainTextureDataByOverlay,
            CancellationToken cancellationToken)
        {
            UploadCallCount++;
            return inner.UploadAsync(
                state,
                importClient,
                preparedCityObject,
                preparedTerrainTextureDataByOverlay,
                cancellationToken);
        }
    }

    private sealed class RecordingPreparedCityObjectImporter : IResonitePreparedCityObjectImporter
    {
        private readonly ResonitePreparedCityObjectImporter inner = new(
            ResoniteLiveSceneImportTargetTestSupport.CreatePreparedTextureUploader(),
            new ResoniteGeometryAssetPlanner(new ResoniteGeometryAssetAssembler()),
            new ResoniteSceneMaterialPlanFactory(new ResoniteMaterialPlanning(CreateBundledDefaultMaterialAssetStore())),
            new ResoniteBatchEmissionPlanner(),
            new PlannedBatchEmissionInterpreter());

        public int ImportCallCount { get; private set; }

        public Task ImportAsync(
            LiveSendRunState state,
            IResoniteLinkClient routedClient,
            QueuedCityObject queuedCityObject,
            PreparedCityObject preparedCityObject,
            ResoniteLinkSendDiagnostics diagnostics,
            Action<string>? progressReporter,
            CancellationToken cancellationToken)
        {
            ImportCallCount++;
            return inner.ImportAsync(
                state,
                routedClient,
                queuedCityObject,
                preparedCityObject,
                diagnostics,
                progressReporter,
                cancellationToken);
        }
    }

    private sealed class RecordingQueuedCityObjectSender : IResoniteQueuedCityObjectSender
    {
        private readonly ResoniteQueuedCityObjectSender inner = new(
            ResoniteLiveSceneImportTargetTestSupport.CreatePreparedCityObjectFactory(),
            new ResonitePreparedCityObjectImporter(
                ResoniteLiveSceneImportTargetTestSupport.CreatePreparedTextureUploader(),
                new ResoniteGeometryAssetPlanner(new ResoniteGeometryAssetAssembler()),
                new ResoniteSceneMaterialPlanFactory(new ResoniteMaterialPlanning(CreateBundledDefaultMaterialAssetStore())),
                new ResoniteBatchEmissionPlanner(),
                new PlannedBatchEmissionInterpreter()));

        public int SendCallCount { get; private set; }

        public Task SendAsync(
            LiveSendRunState state,
            IResoniteLinkClient routedClient,
            QueuedCityObject queuedCityObject,
            ResoniteLinkSendDiagnostics diagnostics,
            Action<string>? progressReporter,
            CancellationToken cancellationToken)
        {
            SendCallCount++;
            return inner.SendAsync(
                state,
                routedClient,
                queuedCityObject,
                diagnostics,
                progressReporter,
                cancellationToken);
        }
    }

    private sealed class RecordingQueuedCityObjectSenderFactory(
        IResoniteQueuedCityObjectSender queuedCityObjectSender) : IResoniteQueuedCityObjectSenderFactory
    {
        public int CreateCallCount { get; private set; }

        public IResoniteQueuedCityObjectSender Create(ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
        {
            ArgumentNullException.ThrowIfNull(terrainTextureAssetGenerator);
            CreateCallCount++;
            return queuedCityObjectSender;
        }
    }

    private sealed class RecordingLiveSendRunFinalizer : IResoniteLiveSendRunFinalizer
    {
        private readonly ResoniteLiveSendRunFinalizer inner = new(new ResoniteCityObjectQueueWriter());

        public int CompleteCallCount { get; private set; }

        public Task<IReadOnlyList<string>> CompleteAsync(
            LiveSendRunState state,
            IResoniteLinkClient routedClient,
            Uri endpoint,
            int connectionCount,
            ResoniteLinkSendDiagnostics diagnostics,
            Action<string>? progressReporter,
            CancellationToken cancellationToken)
        {
            CompleteCallCount++;
            return inner.CompleteAsync(
                state,
                routedClient,
                endpoint,
                connectionCount,
                diagnostics,
                progressReporter,
                cancellationToken);
        }
    }

    private sealed class RecordingLiveSendExecutionResultFactory : IResoniteLiveSendExecutionResultFactory
    {
        private readonly ResoniteLiveSendExecutionResultFactory inner = new();

        public int CreateCallCount { get; private set; }

        public SceneImportExecutionResult Create(
            IReadOnlyList<string> destinations,
            LiveSendRunState state)
        {
            CreateCallCount++;
            return inner.Create(destinations, state);
        }
    }

    private sealed class RecordingLiveSendRunResourceReleaser : IResoniteLiveSendRunResourceReleaser
    {
        private readonly ResoniteLiveSendRunResourceReleaser inner = new();

        public int ReleaseCallCount { get; private set; }

        public ValueTask ReleaseAsync(
            LiveSendRunState? state,
            ILiveSendClientSession clientSession,
            bool disposeClients,
            bool resetClients)
        {
            ReleaseCallCount++;
            return inner.ReleaseAsync(
                state,
                clientSession,
                disposeClients,
                resetClients);
        }
    }

    private sealed class RecordingLiveSendExecutionGateFactory : IResoniteLiveSendExecutionGateFactory
    {
        public RecordingLiveSendExecutionGate Gate { get; } = new();

        public int CreateCallCount { get; private set; }

        public IResoniteLiveSendExecutionGate Create()
        {
            CreateCallCount++;
            return Gate;
        }
    }

    private sealed class RecordingLiveSendExecutionGate : IResoniteLiveSendExecutionGate
    {
        private readonly IResoniteLiveSendExecutionGate inner = new ResoniteLiveSendExecutionGate();

        public int EnterCallCount { get; private set; }

        public int ReleaseCallCount { get; private set; }

        public IDisposable Enter()
        {
            EnterCallCount++;
            return new Lease(inner.Enter(), this);
        }

        private sealed class Lease(
            IDisposable inner,
            RecordingLiveSendExecutionGate gate) : IDisposable
        {
            private bool disposed;

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                inner.Dispose();
                gate.ReleaseCallCount++;
            }
        }
    }

    private sealed class RecordingLiveSendRunStarter : IResoniteLiveSendRunStarter
    {
        private readonly ResoniteLiveSendRunStarter inner = new(
            new ResoniteLiveSendConnectionInitializer(),
            new ResoniteLiveSendSceneSetupRunner(
                new ResoniteSceneSetupInterpreter(new ResoniteSceneSlotLocator(), new ResoniteSceneAnchorResolver()),
                new ResoniteSharedSlotIndexFactory(),
                new ResoniteSlotCreator()),
            new ResoniteLiveSendWorkerLauncher(new ResoniteCityObjectSendWorkerPool()),
            new LiveSendRunPlanFactory(),
            new ResoniteCommonMaterialSetupAssetPreparer(new ResoniteMaterialPlanning(CreateBundledDefaultMaterialAssetStore())),
            new LiveSendRunStateFactory(new ResoniteBufferedCityObjectBakerFactory()),
            new ResoniteTextureImageLoaderFactory());

        public int StartCallCount { get; private set; }

        public Task<LiveSendRunState> StartAsync(
            LiveSendRunStartRequest request,
            CancellationToken cancellationToken)
        {
            StartCallCount++;
            return inner.StartAsync(request, cancellationToken);
        }
    }

    private sealed class RecordingLiveSendConnectionInitializer : IResoniteLiveSendConnectionInitializer
    {
        private readonly ResoniteLiveSendConnectionInitializer inner = new();

        public int EnsureConnectedCallCount { get; private set; }

        public ResoniteSceneSetupInfo? LastSetupInfo { get; private set; }

        public PlateauImportRequest? LastNormalizedRequest { get; private set; }

        public Task EnsureConnectedAsync(
            ILiveSendClientSession clientSession,
            Uri endpoint,
            int connectionCount,
            ResoniteSceneSetupInfo setupInfo,
            PlateauImportRequest normalizedRequest,
            Action<string>? progressReporter,
            CancellationToken cancellationToken)
        {
            EnsureConnectedCallCount++;
            LastSetupInfo = setupInfo;
            LastNormalizedRequest = normalizedRequest;
            return inner.EnsureConnectedAsync(
                clientSession,
                endpoint,
                connectionCount,
                setupInfo,
                normalizedRequest,
                progressReporter,
                cancellationToken);
        }
    }

    private sealed class RecordingSceneMaterialPlanFactory : IResoniteSceneMaterialPlanFactory
    {
        private readonly ResoniteSceneMaterialPlanFactory inner =
            new(new ResoniteMaterialPlanning(CreateBundledDefaultMaterialAssetStore()));

        public int CreateCallCount { get; private set; }

        public Task<PlannedSceneMaterialPlan> CreateAsync(
            LiveSendRunState state,
            IResoniteLinkClient importClient,
            ResoniteConstructionCityObject cityObject,
            UploadedTextureAssetSet uploadedTextures,
            Action<string>? reportStep,
            CancellationToken cancellationToken)
        {
            CreateCallCount++;
            return inner.CreateAsync(
                state,
                importClient,
                cityObject,
                uploadedTextures,
                reportStep,
                cancellationToken);
        }
    }

    private sealed class RecordingCityObjectQueueWriter : IResoniteCityObjectQueueWriter
    {
        private readonly ResoniteCityObjectQueueWriter inner = new();

        public int QueueObjectUnitCallCount { get; private set; }

        public Task QueueObjectUnitAsync(
            LiveSendRunState state,
            ImportedObjectUnit objectUnit,
            IResoniteLinkClient routedClient,
            int connectionCount,
            Action<string>? progressReporter,
            CancellationToken cancellationToken)
        {
            QueueObjectUnitCallCount++;
            return inner.QueueObjectUnitAsync(
                state,
                objectUnit,
                routedClient,
                connectionCount,
                progressReporter,
                cancellationToken);
        }

        public Task<int> FlushBufferedCityObjectsAsync(
            LiveSendRunState state,
            CompositeCityObjectBaker cityObjectBaker,
            IResoniteLinkClient routedClient,
            int connectionCount,
            Action<string>? progressReporter,
            CancellationToken cancellationToken)
        {
            return inner.FlushBufferedCityObjectsAsync(
                state,
                cityObjectBaker,
                routedClient,
                connectionCount,
                progressReporter,
                cancellationToken);
        }
    }

    private sealed class RecordingImportedObjectUnitStreamQueueWriter : IResoniteImportedObjectUnitStreamQueueWriter
    {
        private readonly ResoniteImportedObjectUnitStreamQueueWriter inner = new(new ResoniteCityObjectQueueWriter());

        public int QueueCallCount { get; private set; }

        public Task QueueAsync(
            LiveSendRunState state,
            IAsyncEnumerable<ImportedObjectUnit> objectUnits,
            IResoniteLinkClient routedClient,
            int connectionCount,
            Action<string>? progressReporter,
            CancellationToken cancellationToken)
        {
            QueueCallCount++;
            return inner.QueueAsync(
                state,
                objectUnits,
                routedClient,
                connectionCount,
                progressReporter,
                cancellationToken);
        }
    }

    private sealed class RecordingCityObjectSendWorkerPool : IResoniteCityObjectSendWorkerPool
    {
        private readonly ResoniteCityObjectSendWorkerPool inner = new();

        public int CreateProcessingTasksCallCount { get; private set; }

        public Task[] CreateProcessingTasks(
            LiveSendRunState state,
            LiveSendExecutionRuntime runtime,
            int connectionCount,
            Uri endpoint,
            Action<string>? progressReporter,
            ResoniteQueuedCityObjectProcessor processQueuedCityObjectAsync)
        {
            CreateProcessingTasksCallCount++;
            return inner.CreateProcessingTasks(
                state,
                runtime,
                connectionCount,
                endpoint,
                progressReporter,
                processQueuedCityObjectAsync);
        }
    }

    private sealed class RecordingLiveSendWorkerLauncher : IResoniteLiveSendWorkerLauncher
    {
        private readonly ResoniteLiveSendWorkerLauncher inner = new(new ResoniteCityObjectSendWorkerPool());

        public int StartCallCount { get; private set; }

        public void Start(
            LiveSendRunState state,
            int connectionCount,
            Uri endpoint,
            Action<string>? progressReporter,
            ResoniteQueuedCityObjectProcessor processQueuedCityObjectAsync,
            ResoniteLinkSendDiagnostics diagnostics)
        {
            StartCallCount++;
            inner.Start(
                state,
                connectionCount,
                endpoint,
                progressReporter,
                processQueuedCityObjectAsync,
                diagnostics);
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
        ResoniteBufferedCityObjectBakerFactory factory = new();
        CompositeCityObjectBaker baker = factory.Create(
                enableMeshBake: true,
                new ResoniteTextureImageLoader(),
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

    private static ResoniteConstructionCityObject CreateValidTriangleBuilding(
        string slotKey,
        string? sourceFileRelativePath = "source.gml")
    {
        return new ResoniteConstructionCityObject(
            SlotKey: slotKey,
            DisplayName: slotKey,
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 1,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: ResoniteLiveSceneImportTargetTestSupport.CreateTriangleMesh(),
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
