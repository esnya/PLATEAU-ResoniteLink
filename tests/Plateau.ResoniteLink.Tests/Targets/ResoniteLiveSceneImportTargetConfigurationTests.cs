
using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Targets;

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
                new ResoniteGeometryAssetAssembler(),
                new ResoniteMaterialPlanning(),
                new ResoniteBatchEmissionPlanner(),
                new PlannedBatchEmissionInterpreter(),
                new ResoniteSlotCreator(),
                new ResoniteBufferedCityObjectBakerFactory()));
    }
}
