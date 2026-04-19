
using System.Diagnostics.CodeAnalysis;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Targets;

public sealed class ResoniteLiveSceneImportTargetConfigurationTests
{
    [Fact]
    public async Task ConstructorEnablesMeshBakeByDefault()
    {
        await using ResoniteLiveSceneImportTarget builder = CreateBuilder();

        Assert.True(builder.MeshBakeEnabled);
    }

    [Fact]
    public async Task ConstructorCanDisableMeshBake()
    {
        await using ResoniteLiveSceneImportTarget builder = CreateBuilder(enableMeshBake: false);

        Assert.False(builder.MeshBakeEnabled);
    }

    [Fact]
    public async Task ConstructorUsesLargeMemoryProfileByDefault()
    {
        await using ResoniteLiveSceneImportTarget builder = CreateBuilder();

        Assert.Equal(PlateauImportMemoryProfile.Large, builder.MemoryProfile);
    }

    [Fact]
    public async Task ConstructorReusesDependencyDiagnostics()
    {
        ResoniteLinkSendDiagnostics diagnostics = ResoniteLinkSendDiagnostics.CreateEnabled();
        await using ResoniteLiveSceneImportTarget builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            diagnostics,
            PlateauImportMemoryProfile.Large,
            new ResoniteLiveSceneImportDependencies(
                new DelegatingClientSession(),
                diagnostics,
                new TerrainTextureAssetGenerator(),
                new ResoniteSceneBootstrapInterpreter(new ResoniteSceneSlotLocator()),
                new ResoniteGeometryAssetAssembler(),
                new ResoniteMaterialPlanning(),
                new ResoniteBatchEmissionPlanner(),
                new PlannedBatchEmissionInterpreter(),
                new ResoniteSlotCreator(),
                new ResoniteBufferedCityObjectBakerFactory()),
            enableMeshBake: true,
            progressReporter: null);

        Assert.Same(diagnostics, builder.Diagnostics);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The created target is disposed via await using in this test.")]
    public async Task FactoryCreateReusesTransportDiagnostics()
    {
        using HttpClient terrainTextureAssetHttpClient = new();
        ISceneImportTarget target = ResoniteSceneImportTargetFactory.Create(
            new Uri("ws://localhost:12345/"),
            connectionCount: 1,
            enableSendMetrics: true,
            memoryProfile: PlateauImportMemoryProfile.Large,
            enableMeshBake: true,
            terrainTileCacheRoot: null,
            disableTerrainTileCache: false,
            terrainTextureAssetHttpClient,
            progressReporter: null);
        await using ResoniteLiveSceneImportTarget builder = Assert.IsType<ResoniteLiveSceneImportTarget>(target);

        Assert.Same(builder.Diagnostics, builder.ClientSession.Diagnostics);
    }

    private static ResoniteLiveSceneImportTarget CreateBuilder(bool enableMeshBake = true)
    {
        return new ResoniteLiveSceneImportTarget(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            PlateauImportMemoryProfile.Large,
            new ResoniteLiveSceneImportDependencies(
                new DelegatingClientSession(),
                new TerrainTextureAssetGenerator()),
            enableMeshBake,
            progressReporter: null);
    }
}
