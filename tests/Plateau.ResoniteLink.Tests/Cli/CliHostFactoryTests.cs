using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class CliHostFactoryTests
{
    [Fact]
    public void CreateResolvesCliApplicationAndFactories()
    {
        using IHost host = CliHostFactory.Create([]);

        Assert.NotNull(host.Services.GetRequiredService<CliApplication>());
        Assert.NotNull(host.Services.GetRequiredService<IImportServiceFactory>());
        Assert.NotNull(host.Services.GetRequiredService<IPlateauDatasetSourceResolverFactory>());
        Assert.NotNull(host.Services.GetRequiredService<ISceneImportTargetFactory>());
    }

    [Fact]
    public void CreateResolvesRegisteredPlateauCityGmlServices()
    {
        using IHost host = CliHostFactory.Create([]);

        Assert.NotNull(host.Services.GetRequiredService<ICityGmlDocumentReader>());
        Assert.NotNull(host.Services.GetRequiredService<IResoniteConstructionSourceFactory>());
        Assert.NotNull(host.Services.GetRequiredService<IArchiveFileLayoutPolicy>());
        Assert.NotNull(host.Services.GetRequiredService<IRemoteArchiveDistributionPolicy>());
        Assert.NotNull(host.Services.GetRequiredService<IPlateauDatasetContentSourceFactory>());
    }

    [Fact]
    public void CreateResolvesResoniteLiveSendFactoryServices()
    {
        using IHost host = CliHostFactory.Create([]);

        Assert.NotNull(host.Services.GetRequiredService<IResoniteLiveSceneImportFactory>());
        Assert.NotNull(host.Services.GetRequiredService<IResoniteBatchEmissionPlanner>());
        Assert.NotNull(host.Services.GetRequiredService<IResoniteSceneBatchEmitter>());
        Assert.NotNull(host.Services.GetRequiredService<IResoniteGeometryAssetAssembler>());
        Assert.NotNull(host.Services.GetRequiredService<IResoniteMaterialPlanning>());
    }

    [Fact]
    public async Task CreateResoniteLiveSendFactoryReusesTransportDiagnostics()
    {
        using IHost host = CliHostFactory.Create([]);
        using IServiceScope scope = host.Services.CreateScope();
        IResoniteLiveSceneImportFactory factory = scope.ServiceProvider.GetRequiredService<IResoniteLiveSceneImportFactory>();
        using HttpClient terrainTextureAssetHttpClient = new();
        await using ResoniteLiveSceneImportTarget builder = factory.CreateTarget(
            new ResoniteLiveSceneImportTargetOptions(
                new Uri("ws://localhost:12345/"),
                ConnectionCount: 1,
                EnableSendMetrics: true,
                MemoryProfile: PlateauImportMemoryProfile.Large,
                EnableMeshBake: true,
                TerrainTileCacheRoot: null,
                DisableTerrainTileCache: false,
                ProgressReporter: null),
            terrainTextureAssetHttpClient);

        Assert.Same(builder.Diagnostics, builder.ClientSession.Diagnostics);
    }
}
