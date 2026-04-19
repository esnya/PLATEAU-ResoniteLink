using Microsoft.Extensions.DependencyInjection;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Profiles.PlateauCityGml;

namespace Plateau.ResoniteLink.Cli;

internal static class PlateauImportServiceCollectionExtensions
{
    public static IServiceCollection AddPlateauCityGmlImportServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ICityGmlDocumentReader>(provider =>
            PlateauCityGmlComposition.CreateDocumentReader(
                provider.GetRequiredService<IPlateauDatasetContentSourceFactory>()));
        services.AddSingleton<IResoniteConstructionSourceFactory>(provider =>
            PlateauCityGmlComposition.CreateConstructionSourceFactory(
                provider.GetRequiredService<IPlateauDatasetContentSourceFactory>()));

        return services;
    }
}
