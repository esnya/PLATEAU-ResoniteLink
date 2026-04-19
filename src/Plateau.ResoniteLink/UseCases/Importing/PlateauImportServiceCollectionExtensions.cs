using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Plateau.ResoniteLink.Profiles.PlateauCityGml;

namespace Plateau.ResoniteLink.Application.Importing;

public static class PlateauImportServiceCollectionExtensions
{
    public static IServiceCollection AddPlateauCityGmlImportServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IArchiveFileLayoutPolicy, ArchiveFileLayoutPolicy>();
        services.TryAddSingleton<IRemoteArchiveDistributionPolicy, RemoteArchiveDistributionPolicy>();
        services.TryAddSingleton<IPlateauDatasetContentSourceFactory, DefaultPlateauDatasetContentSourceFactory>();
        services.AddSingleton<IDefaultMaterialResolver>(_ =>
            PlateauCityGmlImportComposition.CreateMaterialResolver());
        services.AddSingleton<IResoniteConstructionComposer>(provider =>
            PlateauCityGmlImportComposition.CreateConstructionComposer(
                PlateauCityGmlImportComposition.CreateGeometryProjector(
                    provider.GetRequiredService<IDefaultMaterialResolver>())));
        services.AddSingleton<ICityGmlDocumentReader>(provider =>
            PlateauCityGmlComposition.CreateDocumentReader(
                provider.GetRequiredService<IPlateauDatasetContentSourceFactory>()));
        services.AddSingleton<IResoniteConstructionSourceFactory>(provider =>
            PlateauCityGmlComposition.CreateConstructionSourceFactory(
                provider.GetRequiredService<IPlateauDatasetContentSourceFactory>()));

        return services;
    }
}
