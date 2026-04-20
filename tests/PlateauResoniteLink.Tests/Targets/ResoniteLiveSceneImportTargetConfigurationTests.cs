
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using Microsoft.Extensions.DependencyInjection;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class ResoniteLiveSceneImportTargetConfigurationTests
{
    [Fact]
    public async Task OptionsConstructorEnablesMeshBakeByDefault()
    {
        await using ResoniteLiveSceneImportTarget builder = CreateBuilder();

        Assert.True(builder.MeshBakeEnabled);
    }

    [Fact]
    public async Task OptionsConstructorCanDisableMeshBake()
    {
        await using ResoniteLiveSceneImportTarget builder = CreateBuilder(enableMeshBake: false);

        Assert.False(builder.MeshBakeEnabled);
    }

    [Fact]
    public async Task OptionsConstructorUsesLargeMemoryProfileByDefault()
    {
        await using ResoniteLiveSceneImportTarget builder = CreateBuilder();

        Assert.Equal(PlateauImportMemoryProfile.Large, builder.MemoryProfile);
    }

    [Fact]
    public async Task OptionsConstructorReusesDependencyDiagnostics()
    {
        ResoniteLinkSendDiagnostics diagnostics = ResoniteLinkSendDiagnostics.CreateEnabled();
        await using ResoniteLiveSceneImportTarget builder = new(
            new ResoniteLiveSceneImportTargetOptions(
                new Uri("ws://localhost:12345/"),
                1,
                EnableSendMetrics: true,
                PlateauImportMemoryProfile.Large,
                EnableMeshBake: true,
                TerrainTileCacheRoot: null,
                DisableTerrainTileCache: false,
                ProgressReporter: null),
            new ResoniteLiveSceneImportDependencies(
                new DelegatingClientSession(),
                diagnostics,
                new TerrainTextureAssetGenerator(),
                new ResoniteSceneBootstrapInterpreter(new ResoniteSceneSlotLocator(), new ResoniteMaterialPlanning(), new ResoniteSceneAnchorResolver()),
                new ResoniteDatasetLicenseWriter(),
                new ResoniteGeometryAssetAssembler(),
                new ResoniteMaterialPlanning(),
                new ResoniteBatchEmissionPlanner(),
                new PlannedBatchEmissionInterpreter(),
                new ResoniteSlotCreator(),
                new ResoniteBufferedCityObjectBakerFactory()));

        Assert.Same(diagnostics, builder.Diagnostics);
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
        ISceneImportTarget target = scope.ServiceProvider
            .GetRequiredService<IResoniteLiveSceneImportFactory>()
            .CreateTarget(
                new ResoniteLiveSceneImportTargetOptions(
                    new Uri("ws://localhost:12345/"),
                    1,
                    EnableSendMetrics: true,
                    MemoryProfile: PlateauImportMemoryProfile.Large,
                    EnableMeshBake: true,
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    ProgressReporter: null),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget builder = Assert.IsType<ResoniteLiveSceneImportTarget>(target);

        Assert.Same(builder.Diagnostics, builder.ClientSession.Diagnostics);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task AddResoniteLiveSendTargetServicesPreservesPreRegisteredSessionFactory()
    {
        RecordingClientSessionFactory sessionFactory = new();
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IResoniteClientSessionFactory>(_ => sessionFactory)
            .AddResoniteLiveSendTargetServices()
            .BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        using HttpClient terrainTextureAssetHttpClient = new();
        ISceneImportTarget target = scope.ServiceProvider
            .GetRequiredService<IResoniteLiveSceneImportFactory>()
            .CreateTarget(
                new ResoniteLiveSceneImportTargetOptions(
                    new Uri("ws://localhost:12345/"),
                    1,
                    EnableSendMetrics: false,
                    MemoryProfile: PlateauImportMemoryProfile.Large,
                    EnableMeshBake: true,
                    TerrainTileCacheRoot: null,
                    DisableTerrainTileCache: false,
                    ProgressReporter: null),
                terrainTextureAssetHttpClient);
        await using ResoniteLiveSceneImportTarget builder = Assert.IsType<ResoniteLiveSceneImportTarget>(target);

        Assert.Same(sessionFactory.LastCreatedSession, builder.ClientSession);
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
        ISceneImportTarget target = scope.ServiceProvider
            .GetRequiredService<IResoniteLiveSceneImportFactory>()
            .CreateTarget(
                new ResoniteLiveSceneImportTargetOptions(
                    new Uri("ws://localhost:12345/"),
                    1,
                    EnableSendMetrics: false,
                    MemoryProfile: PlateauImportMemoryProfile.Large,
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

    [Theory]
    [InlineData(PlateauImportMemoryProfile.Small, 512, 32768, 256)]
    [InlineData(PlateauImportMemoryProfile.Large, 4096, 65535, 1024)]
    public void BufferedCityObjectBakerFactoryScalesMeshBakeBudgetsByMemoryProfile(
        PlateauImportMemoryProfile memoryProfile,
        int expectedMaxCityObjectsPerBatch,
        int expectedMaxVerticesPerBatch,
        int expectedMaxBufferedCells)
    {
        ResoniteBufferedCityObjectBakerFactory factory = new();

        CompositeCityObjectBaker baker = factory.Create(
                enableMeshBake: true,
                new ResoniteTextureImageLoader(),
                ResoniteImportBudgetProfiles.ForProfile(memoryProfile))
            ?? throw new InvalidOperationException("Expected mesh bake composite baker.");

        IResoniteBufferedCityObjectBaker fixedCellBaker = Assert.Single(
            GetPrivateField<IResoniteBufferedCityObjectBaker[]>(baker, "bakers"),
            static candidate => candidate is FixedCellCityObjectMeshBaker);

        Assert.Equal(expectedMaxCityObjectsPerBatch, GetPrivateField<int>(fixedCellBaker, "maxCityObjectsPerBatch"));
        Assert.Equal(expectedMaxVerticesPerBatch, GetPrivateField<int>(fixedCellBaker, "maxVerticesPerBatch"));
        Assert.Equal(expectedMaxBufferedCells, GetPrivateField<int>(fixedCellBaker, "maxBufferedCells"));
    }

    private static ResoniteLiveSceneImportTarget CreateBuilder(bool enableMeshBake = true)
    {
        ResoniteLinkSendDiagnostics diagnostics = ResoniteLinkSendDiagnostics.Disabled;
        return new ResoniteLiveSceneImportTarget(
            new ResoniteLiveSceneImportTargetOptions(
                new Uri("ws://localhost:12345/"),
                1,
                EnableSendMetrics: false,
                PlateauImportMemoryProfile.Large,
                enableMeshBake,
                TerrainTileCacheRoot: null,
                DisableTerrainTileCache: false,
                ProgressReporter: null),
            new ResoniteLiveSceneImportDependencies(
                new DelegatingClientSession(),
                diagnostics,
                new TerrainTextureAssetGenerator(),
                new ResoniteSceneBootstrapInterpreter(new ResoniteSceneSlotLocator(), new ResoniteMaterialPlanning(), new ResoniteSceneAnchorResolver()),
                new ResoniteDatasetLicenseWriter(),
                new ResoniteGeometryAssetAssembler(),
                new ResoniteMaterialPlanning(),
                new ResoniteBatchEmissionPlanner(),
                new PlannedBatchEmissionInterpreter(),
                new ResoniteSlotCreator(),
                new ResoniteBufferedCityObjectBakerFactory()));
    }

    private sealed class RecordingClientSessionFactory : IResoniteClientSessionFactory
    {
        public ILiveSendClientSession? LastCreatedSession { get; private set; }

        public ILiveSendClientSession Create(ResoniteLiveSceneImportTargetOptions options, ResoniteLinkSendDiagnostics diagnostics)
        {
            _ = options;
            LastCreatedSession = new DelegatingClientSession();
            return LastCreatedSession;
        }
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

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field '{fieldName}' was not found on '{instance.GetType().Name}'.");
        return (T)(field.GetValue(instance)
            ?? throw new InvalidOperationException($"Field '{fieldName}' was null."));
    }
}
