using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Cli;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class CliHostTests
{
    [Fact]
    public void BuildHostRegistersCliApplicationAndResolver()
    {
        using IHost host = CliHost.BuildHost();

        CliApplication application = host.Services.GetRequiredService<CliApplication>();
        IPlateauDatasetSourceResolver resolver = host.Services.GetRequiredService<IPlateauDatasetSourceResolver>();

        Assert.NotNull(application);
        Assert.IsType<CkanPlateauDatasetSourceResolver>(resolver);
    }
}
