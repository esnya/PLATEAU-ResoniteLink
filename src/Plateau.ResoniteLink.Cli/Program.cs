using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Plateau.ResoniteLink.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using IHost host = CliHostFactory.Create(args);
        return await host.Services.GetRequiredService<CliApplication>().RunAsync(args);
    }
}
