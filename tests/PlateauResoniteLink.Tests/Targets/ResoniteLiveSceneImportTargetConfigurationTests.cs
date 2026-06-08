
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging.Abstractions;

using Microsoft.Extensions.DependencyInjection;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Targets.Resonite.Diagnostics;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class ResoniteLiveSceneImportTargetConfigurationTests
{
    private static BundledDefaultMaterialAssetStore CreateBundledDefaultMaterialAssetStore() => new();

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
                TerrainTileCacheRoot: null,
                DisableTerrainTileCache: false,
                LoggerFactory: NullLoggerFactory.Instance),
            new DelegatingClientSession(),
            diagnostics,
            ResoniteLiveSceneImportTargetTestSupport.CreateRunExecutor(
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
            .GetRequiredService<ResoniteLiveSceneImportFactory>()
            .CreateTarget(
                new ResoniteLiveSceneImportTargetOptions(
                    new Uri("ws://localhost:12345/"),
                    1,
                    EnableSendMetrics: true,
                    MemoryProfile: ResoniteImportMemoryProfile.Large,
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    LoggerFactory: NullLoggerFactory.Instance),
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
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    LoggerFactory: NullLoggerFactory.Instance),
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
    public void AddResoniteLiveSendTargetServicesPreservesPreRegisteredSlotCreation()
    {
        CreateResoniteSlot createResoniteSlot =
            static (client, parent, slotName, position, rotation, cancellationToken) =>
            {
                _ = client;
                _ = parent;
                _ = slotName;
                _ = position;
                _ = rotation;
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(new CreatedSlot(new ResoniteSlotLocator("custom-slot"), "custom"));
            };

        ServiceProvider provider = new ServiceCollection()
            .AddScoped(_ => createResoniteSlot)
            .AddResoniteLiveSendTargetServices()
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        Assert.Same(createResoniteSlot, scope.ServiceProvider.GetRequiredService<CreateResoniteSlot>());
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredTerrainTextureGenerator()
    {
        int createCallCount = 0;
        ResoniteLiveSceneImportTargetOptions? recordedOptions = null;
        HttpClient? recordedHttpClient = null;
        GenerateTerrainTexture generateTerrainTexture = static (terrainTextureOverlay, cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(terrainTextureOverlay);
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The configuration test should not generate terrain textures.");
        };
        CreateTerrainTextureGenerator createTerrainTextureGenerator = (options, terrainTextureAssetHttpClient) =>
        {
            createCallCount++;
            recordedOptions = options;
            recordedHttpClient = terrainTextureAssetHttpClient;
            return generateTerrainTexture;
        };

        ServiceProvider provider = new ServiceCollection()
            .AddScoped(_ => createTerrainTextureGenerator)
            .AddResoniteLiveSendTargetServices()
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        using HttpClient terrainTextureAssetHttpClient = new();
        ResoniteLiveSceneImportTargetOptions targetOptions = new(
            new Uri("ws://localhost:12345/"),
            1,
            EnableSendMetrics: false,
            MemoryProfile: ResoniteImportMemoryProfile.Large,
            TerrainTileCacheRoot: null,
            DisableTerrainTileCache: false,
            ProgressReporter: null);
        ISceneSink target = scope.ServiceProvider
            .GetRequiredService<ResoniteLiveSceneImportFactory>()
            .CreateTarget(targetOptions, terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget importTarget = Assert.IsType<ResoniteLiveSceneImportTarget>(target);

        Assert.NotNull(importTarget);
        Assert.Equal(1, createCallCount);
        Assert.Same(targetOptions, recordedOptions);
        Assert.Same(terrainTextureAssetHttpClient, recordedHttpClient);
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
                TerrainTileCacheRoot: null,
                DisableTerrainTileCache: false,
                LoggerFactory: NullLoggerFactory.Instance),
            outputPath);

        Assert.Equal(1, importFactory.PreconfiguredCreateCallCount);
        Assert.NotNull(importFactory.LastClientSession);
        Assert.Same(ResoniteLinkSendDiagnostics.Disabled, importFactory.LastDiagnostics);
        Assert.NotNull(importFactory.LastGenerateTerrainTexture);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredRunSetupPreparer()
    {
        RecordingRunSetupPreparer runSetupPreparer = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteLiveSendRunSetupPreparer>(_ => runSetupPreparer)
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
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    LoggerFactory: NullLoggerFactory.Instance),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget _ = Assert.IsType<ResoniteLiveSceneImportTarget>(target);

        Assert.Same(runSetupPreparer, scope.ServiceProvider.GetRequiredService<IResoniteLiveSendRunSetupPreparer>());
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredRunExecutorFactory()
    {
        RecordingRunExecutorFactory runExecutorFactory = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteLiveSendRunExecutorFactory>(_ => runExecutorFactory)
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
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    LoggerFactory: NullLoggerFactory.Instance),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget importTarget = Assert.IsType<ResoniteLiveSceneImportTarget>(target);

        Assert.Equal(1, runExecutorFactory.CreateCallCount);
        Assert.NotNull(runExecutorFactory.LastRunStarter);
        Assert.Same(runExecutorFactory.Executor, importTarget.RunExecutor);
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
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    LoggerFactory: NullLoggerFactory.Instance),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget importTarget = Assert.IsType<ResoniteLiveSceneImportTarget>(target);

        Assert.NotNull(importTarget.ClientSession);
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


    private static ResoniteLiveSceneImportTarget CreateImportTarget()
    {
        ResoniteLinkSendDiagnostics diagnostics = ResoniteLinkSendDiagnostics.Disabled;
        ResoniteMaterialPlanning materialPlanning = new(CreateBundledDefaultMaterialAssetStore());
        return new ResoniteLiveSceneImportTarget(
            new ResoniteLiveSceneImportTargetOptions(
                new Uri("ws://localhost:12345/"),
                1,
                EnableSendMetrics: false,
                ResoniteImportMemoryProfile.Large,
                TerrainTileCacheRoot: null,
                DisableTerrainTileCache: false,
                LoggerFactory: NullLoggerFactory.Instance),
            new DelegatingClientSession(),
            diagnostics,
            ResoniteLiveSceneImportTargetTestSupport.CreateRunExecutor(
                ResoniteLiveSceneImportTargetTestSupport.CreateRunStarter(materialPlanning)));
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
                        generateTerrainTexture: generateTerrainTexture)));
        }
    }

    private sealed class RecordingRunExecutorFactory : IResoniteLiveSendRunExecutorFactory
    {
        public int CreateCallCount { get; private set; }

        public ResoniteLiveSendRunStarter? LastRunStarter { get; private set; }

        public IResoniteLiveSendRunExecutor Executor { get; } = new ThrowingRunExecutor();

        public IResoniteLiveSendRunExecutor Create(ResoniteLiveSendRunStarter runStarter)
        {
            CreateCallCount++;
            LastRunStarter = runStarter;
            return Executor;
        }
    }

    private sealed class ThrowingRunExecutor : IResoniteLiveSendRunExecutor
    {
        public Task<SceneImportExecutionResult> ExecuteAsync(
            LiveSendRunStartRequest request,
            IAsyncEnumerable<ImportedObjectUnit> objectUnits,
            LiveSendRunExecutionContext context,
            CancellationToken cancellationToken)
        {
            _ = request;
            _ = objectUnits;
            _ = context;
            cancellationToken.ThrowIfCancellationRequested();
            throw new NotSupportedException("This test only verifies DI override preservation during target creation.");
        }
    }

    private sealed class RecordingRunSetupPreparer : IResoniteLiveSendRunSetupPreparer
    {
        public Task<LiveSendPreparedRunSetup> PrepareAsync(
            LiveSendRunPlan runPlan,
            LiveSendRunStartRequest request,
            LiveSendRunStartContext context,
            CancellationToken cancellationToken)
        {
            _ = runPlan;
            _ = request;
            _ = context;
            cancellationToken.ThrowIfCancellationRequested();
            throw new NotSupportedException("This test only verifies DI override preservation during target creation.");
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
                new NonDemAtlasBakeBudget(ResourceBudget: ResoniteImportBudgetProfiles.ForProfile(memoryProfile)),
                CreateRequestLocalOrigin("53394525")));

        int readyBeforeFlush = 0;
        for (int index = 0; index < cityObjectCount; index++)
        {
            IReadOnlyList<ResoniteConstructionCityObject> ready = await baker.BufferAsync(createCityObject(index));
            readyBeforeFlush += ready.Count;
        }

        return readyBeforeFlush;
    }

    private static NonDemSourceFileBakeEmitter CreateSourceFileBakeEmitter(
        NonDemAtlasBakeBudget atlasBudget,
        ResoniteLocalOrigin requestLocalOrigin)
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
                new NonDemBakedGeometryComposer(requestLocalOrigin)),
            layoutFactory);
    }

    private static ResoniteLocalOrigin CreateRequestLocalOrigin(string meshCode)
    {
        Assert.True(PlateauMeshCode.TryGetGeodeticCenter(meshCode, out GeodeticCoordinate center));
        return new ResoniteLocalOrigin(center.Latitude, center.Longitude, center.Altitude);
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
}
