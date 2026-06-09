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
using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Targets.Resonite.Diagnostics;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

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
            new DelegatingClientSession(),
            diagnostics,
            ResoniteLiveSceneImportTargetTestSupport.CreateRunExecutor(
                ResoniteLiveSceneImportTargetTestSupport.CreateRunStarter(materialPlanning)),
            ResoniteLiveSendRunResourceReleaser.ReleaseAsync);

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
            .GetRequiredService<ResoniteLiveSceneImportFactory>()
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
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredSessionCreation()
    {
        ILiveSendClientSession? recordedSession = null;
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<Func<ResoniteLiveSceneImportTargetOptions, ResoniteLinkSendDiagnostics, ILiveSendClientSession>>(
                _ => new Func<ResoniteLiveSceneImportTargetOptions, ResoniteLinkSendDiagnostics, ILiveSendClientSession>((options, diagnostics) =>
                {
                    ArgumentNullException.ThrowIfNull(options);
                    ArgumentNullException.ThrowIfNull(diagnostics);
                    return recordedSession = new DelegatingClientSession();
                }))
            .AddResoniteLiveSendTargetServices()
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        using HttpClient terrainTextureAssetHttpClient = new();
        ISceneSink target = scope.ServiceProvider
            .GetRequiredService<ResoniteLiveSceneImportFactory>()
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
    public void AddResoniteLiveSendTargetServicesPreservesPreRegisteredSetupResolvers()
    {
        ResolveResoniteDatasetRootSlot resolveDatasetRootSlot =
            static (setupClient, datasetRootName, cancellationToken) =>
            {
                _ = setupClient;
                _ = datasetRootName;
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult<CreatedSlot?>(null);
            };
        ResolveResoniteSceneAnchor resolveSceneAnchor =
            static (setupClient, datasetRootSlot, completionMeshCode, cancellationToken) =>
            {
                _ = setupClient;
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(new SceneAnchor(
                    datasetRootSlot,
                    completionMeshCode,
                    new ResoniteFloat3(0.0, 0.0, 0.0),
                    ReferenceSourceFileRoot: null));
            };

        ServiceProvider provider = new ServiceCollection()
            .AddScoped(_ => resolveDatasetRootSlot)
            .AddScoped(_ => resolveSceneAnchor)
            .AddResoniteLiveSendTargetServices()
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Assert.Same(resolveDatasetRootSlot, scope.ServiceProvider.GetRequiredService<ResolveResoniteDatasetRootSlot>());
        Assert.Same(resolveSceneAnchor, scope.ServiceProvider.GetRequiredService<ResolveResoniteSceneAnchor>());
    }

    [Fact]
    public void AddResoniteLiveSendTargetServicesPreservesPreRegisteredConnectionInitializer()
    {
        EnsureResoniteLiveSendConnected ensureConnected =
            static (request, runPlan, context, cancellationToken) =>
            {
                _ = request;
                _ = runPlan;
                _ = context;
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            };

        ServiceProvider provider = new ServiceCollection()
            .AddScoped(_ => ensureConnected)
            .AddResoniteLiveSendTargetServices()
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Assert.Same(ensureConnected, scope.ServiceProvider.GetRequiredService<EnsureResoniteLiveSendConnected>());
    }

    [Fact]
    public void AddResoniteLiveSendTargetServicesPreservesPreRegisteredGsiFallbackLicenseWriter()
    {
        EnsureResoniteGsiFallbackLicense ensureGsiFallbackLicense =
            static (client, datasetRootSlot, cancellationToken) =>
            {
                _ = client;
                _ = datasetRootSlot;
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            };

        ServiceProvider provider = new ServiceCollection()
            .AddScoped(_ => ensureGsiFallbackLicense)
            .AddResoniteLiveSendTargetServices()
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Assert.Same(ensureGsiFallbackLicense, scope.ServiceProvider.GetRequiredService<EnsureResoniteGsiFallbackLicense>());
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredTerrainTextureGenerator()
    {
        HttpClient? recordedClient = null;
        string? recordedCacheRoot = null;
        bool? recordedDisablePersistentCache = null;
        CreateTerrainTextureGenerator createTerrainTextureGenerator = (httpClient, cacheRootPath, disablePersistentCache) =>
        {
            recordedClient = httpClient;
            recordedCacheRoot = cacheRootPath;
            recordedDisablePersistentCache = disablePersistentCache;
            return static (_, _) => Task.FromException<GeneratedTerrainTexture>(
                new NotSupportedException("The configuration test only verifies factory wiring."));
        };

        ServiceProvider provider = new ServiceCollection()
            .AddScoped(_ => createTerrainTextureGenerator)
            .AddResoniteLiveSendTargetServices()
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        using HttpClient terrainTextureAssetHttpClient = new();
        await using ResoniteLiveSceneImportTarget importTarget = Assert.IsType<ResoniteLiveSceneImportTarget>(
            scope.ServiceProvider
                .GetRequiredService<ResoniteLiveSceneImportFactory>()
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
                    terrainTextureAssetHttpClient));

        Assert.Same(terrainTextureAssetHttpClient, recordedClient);
        Assert.Equal("cache-root", recordedCacheRoot);
        Assert.True(recordedDisablePersistentCache);
    }

    [Fact]
    public void AddResoniteLiveSendTargetServicesPreservesPreRegisteredSlotCreation()
    {
        CreateResoniteSlot createSlot =
            static (client, parent, slotName, position, rotation, cancellationToken) =>
            {
                _ = client;
                _ = parent;
                _ = slotName;
                _ = position;
                _ = rotation;
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(new CreatedSlot(new ResoniteSlotLocator("slot-id"), slotName));
            };

        ServiceProvider provider = new ServiceCollection()
            .AddScoped(_ => createSlot)
            .AddResoniteLiveSendTargetServices()
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Assert.Same(createSlot, scope.ServiceProvider.GetRequiredService<CreateResoniteSlot>());
    }

    [Fact]
    public void AddResoniteLiveSendTargetServicesPreservesPreRegisteredCityObjectBakerFactory()
    {
        CreateNonDemCityObjectBaker createCityObjectBaker = static (enableMeshBake, resourceBudget, requestLocalOrigin) =>
        {
            _ = enableMeshBake;
            _ = resourceBudget;
            _ = requestLocalOrigin;
            return null;
        };

        ServiceProvider provider = new ServiceCollection()
            .AddScoped(_ => createCityObjectBaker)
            .AddResoniteLiveSendTargetServices()
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Assert.Same(createCityObjectBaker, scope.ServiceProvider.GetRequiredService<CreateNonDemCityObjectBaker>());
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredRunStarterFactory()
    {
        GenerateTerrainTexture? recordedGenerateTerrainTexture = null;
        ResoniteLiveSendRunStarter runStarter = ResoniteLiveSceneImportTargetTestSupport.CreateRunStarter(
            new ResoniteMaterialPlanning(CreateBundledDefaultMaterialAssetStore()));
        CreateResoniteLiveSendRunStarter createRunStarter = generateTerrainTexture =>
        {
            recordedGenerateTerrainTexture = generateTerrainTexture;
            return runStarter;
        };

        ServiceProvider provider = new ServiceCollection()
            .AddScoped(_ => createRunStarter)
            .AddResoniteLiveSendTargetServices()
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        using HttpClient terrainTextureAssetHttpClient = new();
        await using ResoniteLiveSceneImportTarget importTarget = Assert.IsType<ResoniteLiveSceneImportTarget>(
            scope.ServiceProvider
                .GetRequiredService<ResoniteLiveSceneImportFactory>()
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
                    terrainTextureAssetHttpClient));

        Assert.NotNull(recordedGenerateTerrainTexture);
    }

    [Fact]
    public void AddResoniteLiveSendTargetServicesPreservesPreRegisteredQueueBoundaries()
    {
        QueueLiveSendUnit queueUnit = static (state, objectUnit, context, cancellationToken) =>
        {
            _ = state;
            _ = objectUnit;
            _ = context;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        };
        CompleteLiveSendQueue completeQueue = static (state, context, cancellationToken) =>
        {
            _ = state;
            _ = context;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new SceneImportExecutionResult(["stub://complete"], 0));
        };
        ReleaseLiveSendRunResources releaseResources = static (state, clientSession, disposeClients, resetClients) =>
        {
            _ = state;
            _ = clientSession;
            _ = disposeClients;
            _ = resetClients;
            return ValueTask.CompletedTask;
        };

        ServiceProvider provider = new ServiceCollection()
            .AddScoped(_ => queueUnit)
            .AddScoped(_ => completeQueue)
            .AddScoped(_ => releaseResources)
            .AddResoniteLiveSendTargetServices()
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Assert.Same(queueUnit, scope.ServiceProvider.GetRequiredService<QueueLiveSendUnit>());
        Assert.Same(completeQueue, scope.ServiceProvider.GetRequiredService<CompleteLiveSendQueue>());
        Assert.Same(releaseResources, scope.ServiceProvider.GetRequiredService<ReleaseLiveSendRunResources>());
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The target is disposed explicitly by the test.")]
    public async Task ImportTargetDisposeUsesInjectedReleaseResources()
    {
        int releaseCallCount = 0;
        ReleaseLiveSendRunResources releaseResources = (state, clientSession, disposeClients, resetClients) =>
        {
            Assert.Null(state);
            Assert.True(disposeClients);
            Assert.False(resetClients);
            Assert.NotNull(clientSession);
            releaseCallCount++;
            return ValueTask.CompletedTask;
        };
        ResoniteMaterialPlanning materialPlanning = new(CreateBundledDefaultMaterialAssetStore());
        ResoniteLiveSceneImportTarget importTarget = new(
            new ResoniteLiveSceneImportTargetOptions(
                new Uri("ws://localhost:12345/"),
                1,
                EnableSendMetrics: false,
                ResoniteImportMemoryProfile.Large,
                EnableMeshBake: true,
                TerrainTileCacheRoot: null,
                DisableTerrainTileCache: false,
                ProgressReporter: null),
            new DelegatingClientSession(),
            ResoniteLinkSendDiagnostics.Disabled,
            ResoniteLiveSceneImportTargetTestSupport.CreateRunExecutor(
                ResoniteLiveSceneImportTargetTestSupport.CreateRunStarter(materialPlanning)),
            releaseResources);

        await importTarget.DisposeAsync();

        Assert.Equal(1, releaseCallCount);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredRunExecutorFactory()
    {
        RecordingRunExecutor runExecutor = new();
        CreateResoniteLiveSendRunExecutor createRunExecutor = _ => runExecutor;
        ServiceProvider provider = new ServiceCollection()
            .AddScoped(_ => createRunExecutor)
            .AddResoniteLiveSendTargetServices()
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        using HttpClient terrainTextureAssetHttpClient = new();
        await using ResoniteLiveSceneImportTarget importTarget = Assert.IsType<ResoniteLiveSceneImportTarget>(
            scope.ServiceProvider
                .GetRequiredService<ResoniteLiveSceneImportFactory>()
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
                    terrainTextureAssetHttpClient));
        using TemporaryDirectory workDirectory = new();
        SceneImportExecutionPlan plan = ResoniteLiveSceneImportTargetTestSupport.CreateExecutionPlan(
            ResoniteLiveSceneImportTargetTestSupport.CreateMetadata(
                "dataset",
                "53394525",
                workDirectory.Path,
                new ResoniteLocalOrigin(35.0, 139.0, 0.0)),
            workDirectory.Path);

        SceneImportExecutionResult result = await importTarget.ExecuteAsync(
            plan,
            ResoniteLiveSceneImportTargetTestSupport.CreateImportedObjectUnitsForTestsAsync([]));

        Assert.Equal(1, runExecutor.ExecuteCallCount);
        Assert.Same(result, runExecutor.Result);
    }

    [Fact]
    public async Task CanonicalDumpCreateUsesProvidedLiveSceneImportFactory()
    {
        RecordingLiveSceneImportFactory importFactory = new(new ResoniteMaterialPlanning(CreateBundledDefaultMaterialAssetStore()));
        using TemporaryDirectory outputDirectory = new();
        string outputPath = Path.Combine(outputDirectory.Path, "scene.json");

        await using ISceneSink _ = CanonicalSceneDumpSink.Create(
            importFactory.CreateTarget,
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
        Assert.NotNull(importFactory.LastGenerateTerrainTexture);
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
            .GetRequiredService<ResoniteLiveSceneImportFactory>()
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

    [Fact]
    public void AddResoniteLiveSendTargetServicesPreservesPreRegisteredTransportFactory()
    {
        RecordingResoniteLinkTransport transport = new();
        try
        {
            int createTransportCallCount = 0;
            using ServiceProvider provider = new ServiceCollection()
                .AddScoped<Func<IResoniteLinkTransport>>(_ => () =>
                {
                    createTransportCallCount++;
                    return transport;
                })
                .AddResoniteLiveSendTargetServices()
                .BuildServiceProvider();
            using IServiceScope scope = provider.CreateScope();
            Func<Action<string>?, IResoniteLinkClient> createClient =
                scope.ServiceProvider.GetRequiredService<Func<Action<string>?, IResoniteLinkClient>>();

            IResoniteLinkClient client = createClient(null);
            client.Dispose();

            Assert.Equal(1, createTransportCallCount);
            Assert.Equal(1, transport.DisposeCallCount);
        }
        finally
        {
            transport.Dispose();
        }
    }

    [Theory]
    [InlineData(ResoniteImportMemoryProfile.Small)]
    [InlineData(ResoniteImportMemoryProfile.Large)]
    public async Task MeshBakeBuffersLod1NonDemObjectsAcrossMemoryProfiles(
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
    public async Task MeshBakeKeepsMeshesAboveUInt16VertexRangeBufferedUntilExplicitFlush(
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
    public async Task MeshBakeSkipsDemObjectsAcrossMemoryProfiles(
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
            new DelegatingClientSession(),
            diagnostics,
            ResoniteLiveSceneImportTargetTestSupport.CreateRunExecutor(
                ResoniteLiveSceneImportTargetTestSupport.CreateRunStarter(materialPlanning)),
            ResoniteLiveSendRunResourceReleaser.ReleaseAsync);
    }

    private sealed class RecordingLiveSceneImportFactory(
        ResoniteMaterialPlanning materialPlanning)
    {
        public int PreconfiguredCreateCallCount { get; private set; }

        public ILiveSendClientSession? LastClientSession { get; private set; }

        public ResoniteLinkSendDiagnostics? LastDiagnostics { get; private set; }

        public GenerateTerrainTexture? LastGenerateTerrainTexture { get; private set; }

        public ResoniteLiveSceneImportTarget CreateTarget(
            ResoniteLiveSceneImportTargetOptions options,
            ILiveSendClientSession clientSession,
            ResoniteLinkSendDiagnostics diagnostics,
            GenerateTerrainTexture generateTerrainTexture)
        {
            PreconfiguredCreateCallCount++;
            LastClientSession = clientSession;
            LastDiagnostics = diagnostics;
            LastGenerateTerrainTexture = generateTerrainTexture;
            return new ResoniteLiveSceneImportTarget(
                options,
                clientSession,
                diagnostics,
                ResoniteLiveSceneImportTargetTestSupport.CreateRunExecutor(
                    ResoniteLiveSceneImportTargetTestSupport.CreateRunStarter(
                        materialPlanning,
                        generateTerrainTexture: generateTerrainTexture)),
                ResoniteLiveSendRunResourceReleaser.ReleaseAsync);
        }
    }

    private static async Task<int> CountReadyBeforeFlushAsync(
        ResoniteImportMemoryProfile memoryProfile,
        int cityObjectCount,
        Func<int, ResoniteConstructionCityObject> createCityObject)
    {
        NonDemCityObjectBaker baker = new(
            NonDemCityObjectBakePolicies.DefaultPolicies,
            CreateSourceFileBakeEmitter(
                new NonDemAtlasBakeBudget(ResourceBudget: ResoniteImportBudgetProfiles.ForProfile(memoryProfile))));

        int readyBeforeFlush = 0;
        for (int index = 0; index < cityObjectCount; index++)
        {
            IReadOnlyList<ResoniteConstructionCityObject> ready = await baker.BufferAsync(createCityObject(index));
            readyBeforeFlush += ready.Count;
        }

        return readyBeforeFlush;
    }

    private static NonDemSourceFileBakeEmitter CreateSourceFileBakeEmitter(NonDemAtlasBakeBudget atlasBudget)
    {
        NonDemAtlasLayoutFactory layoutFactory = new(
            atlasBudget.EffectiveMaxAtlasSize,
            atlasBudget.TilePaddingPixels);
        return new NonDemSourceFileBakeEmitter(
            new NonDemCityObjectBakeCandidateFactory(
                new NonDemBakeEntryFactory(new ResoniteTextureImageLoader(), atlasBudget.EffectiveMaxAtlasTextureEdge)),
            new NonDemCityObjectBakeAssembler(
                layoutFactory,
                new NonDemAtlasImageRenderer(atlasBudget.TilePaddingPixels),
                new ResoniteLocalOrigin(35.0, 139.0, 0.0)),
            new NonDemAtlasBatchFitPolicy(layoutFactory));
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
                    [0],
                    ResoniteMaterialAssetBinding.Presentation),
            ],
            SourceFileRelativePath: sourceFileRelativePath);
    }

    private sealed class RecordingRunExecutor : IResoniteLiveSendRunExecutor
    {
        public SceneImportExecutionResult Result { get; } = new(["stub://executor"], 0);

        public int ExecuteCallCount { get; private set; }

        public Task<SceneImportExecutionResult> ExecuteAsync(
            LiveSendRunStartRequest request,
            IAsyncEnumerable<ImportedObjectUnit> objectUnits,
            LiveSendRunExecutionContext context,
            System.Threading.CancellationToken cancellationToken)
        {
            _ = request;
            _ = objectUnits;
            _ = context;
            cancellationToken.ThrowIfCancellationRequested();
            ExecuteCallCount++;
            return Task.FromResult(Result);
        }
    }

    private sealed class RecordingResoniteLinkTransport : IResoniteLinkTransport
    {
        private bool disposed;

        public int DisposeCallCount { get; private set; }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            DisposeCallCount++;
        }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<NewEntityId> AddComponentAsync(AddComponent request)
        {
            throw new NotSupportedException();
        }

        public Task<NewEntityId> AddSlotAsync(AddSlot request)
        {
            throw new NotSupportedException();
        }

        public Task<BatchResponse> RunDataModelOperationBatchAsync(List<DataModelOperation> operations)
        {
            throw new NotSupportedException();
        }

        public Task<ComponentData> GetComponentDataAsync(GetComponent request)
        {
            throw new NotSupportedException();
        }

        public Task<SlotData> GetSlotDataAsync(GetSlot request)
        {
            throw new NotSupportedException();
        }

        public Task<AssetData> ImportMeshAsync(ImportMeshRawData request)
        {
            throw new NotSupportedException();
        }

        public Task<AssetData> ImportTextureRawAsync(ImportTexture2DRawData request)
        {
            throw new NotSupportedException();
        }

        public Task<AssetData> ImportTextureRawHdrAsync(ImportTexture2DRawDataHDR request)
        {
            throw new NotSupportedException();
        }

        public Task<Response> UpdateComponentAsync(UpdateComponent request)
        {
            throw new NotSupportedException();
        }
    }
}
