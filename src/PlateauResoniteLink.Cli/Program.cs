using System;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PlateauResoniteLink.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using IHost host = CliHostFactory.Create(args);
        await host.StartAsync();
        IHostApplicationLifetime lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        try
        {
            return await host.Services.GetRequiredService<CliApplication>().RunAsync(args, lifetime.ApplicationStopping);
        }
        catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested)
        {
            return 130;
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
