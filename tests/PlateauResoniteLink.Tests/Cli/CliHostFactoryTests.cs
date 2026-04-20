using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using PlateauResoniteLink.Cli;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Cli;

public sealed class CliHostFactoryTests
{
    [Fact]
    public void CreateBuildsCliHost()
    {
        using IHost host = CliHostFactory.Create([]);

        Assert.NotNull(host.Services.GetRequiredService<CliApplication>());
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
