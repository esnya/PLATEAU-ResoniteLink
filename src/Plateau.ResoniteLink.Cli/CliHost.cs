using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Plateau.ResoniteLink.Application.Importing;

namespace Plateau.ResoniteLink.Cli;

internal static class CliHost
{
    public static IHost BuildHost()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IPlateauDatasetSourceResolver, CkanPlateauDatasetSourceResolver>();
        builder.Services.AddSingleton(sp => new CliApplication(
            Console.Out,
            Console.Error,
            options => CliApplication.CreateImportService(
                options,
                sp.GetRequiredService<IPlateauDatasetSourceResolver>())));

        return builder.Build();
    }
}
