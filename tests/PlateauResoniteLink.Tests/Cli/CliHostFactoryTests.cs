using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Cli;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;

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
        ResoniteLiveSceneImportFactory factory = scope.ServiceProvider.GetRequiredService<ResoniteLiveSceneImportFactory>();
        using HttpClient terrainTextureAssetHttpClient = new();
        await using ResoniteLiveSceneImportTarget importTarget = factory.CreateTarget(
            new ResoniteLiveSceneImportTargetOptions(
                new Uri("ws://localhost:12345/"),
                ConnectionCount: 1,
                EnableSendMetrics: true,
                MemoryProfile: ResoniteImportMemoryProfile.Large,
                EnableMeshBake: true,
                TerrainTileCacheRoot: null,
                DisableTerrainTileCache: false,
                LoggerFactory: NullLoggerFactory.Instance),
            terrainTextureAssetHttpClient);

        Assert.Same(importTarget.Diagnostics, importTarget.ClientSession.Diagnostics);
    }

    [Fact]
    public async Task CreateSceneSinkBuildsCanonicalDumpSinkThroughRegisteredFactory()
    {
        using IHost host = CliHostFactory.Create([]);
        Func<ImportCommandOptions, ILoggerFactory, ISceneSink> createSceneSink =
            host.Services.GetRequiredService<Func<ImportCommandOptions, ILoggerFactory, ISceneSink>>();

        await using ISceneSink sink = createSceneSink(
            new ImportCommandOptions(
                new PlateauImportRequest(
                    Dataset: "tokyo23ku",
                    MeshCode: "53394525",
                    CityGmlSource: DatasetLocation.Local(Path.GetTempPath())),
                WorkRoot: Path.GetTempPath(),
                ResoniteLinkUri: null,
                ResoniteLinkConnectionCount: 4,
                MemoryProfile: PlateauImportMemoryProfile.Large,
                EnableMeshBake: true,
                TerrainTileCacheRoot: null,
                DisableTerrainTileCache: false,
                CanonicalSceneDumpPath: Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json"),
                EnableSendMetrics: true,
                VerboseLogging: false),
            NullLoggerFactory.Instance);

        Assert.NotNull(sink);
    }
}
