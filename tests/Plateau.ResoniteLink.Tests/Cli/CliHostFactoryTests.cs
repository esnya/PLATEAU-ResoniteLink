using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Cli;

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
}
