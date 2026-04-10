using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Plateau.ResoniteLink.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using IHost host = CliHost.BuildHost();
        return await host.Services
            .GetRequiredService<CliApplication>()
            .RunAsync(args);
    }
}
