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
        services.AddSingleton<ICityGmlAppearanceStoreFactory, CityGmlAppearanceStoreFactory>();
        services.AddSingleton<ICityGmlLodSelector, CityGmlLodSelector>();
        services.AddSingleton<IDefaultMaterialResolver>(_ =>
            PlateauCityGmlImportComposition.CreateMaterialResolver());
        services.AddSingleton<IResoniteConstructionComposer>(provider =>
            PlateauCityGmlImportComposition.CreateConstructionComposer(
                PlateauCityGmlImportComposition.CreateGeometryProjector(
                    provider.GetRequiredService<IDefaultMaterialResolver>())));
        services.AddSingleton<ICityGmlDocumentReader>(provider =>
            PlateauCityGmlImportComposition.CreateDocumentReader(
                provider.GetRequiredService<IPlateauDatasetContentSourceFactory>(),
                provider.GetRequiredService<ICityGmlAppearanceStoreFactory>(),
                provider.GetRequiredService<ICityGmlLodSelector>()));
        services.AddSingleton<IResoniteConstructionSourceFactory>(provider =>
            PlateauCityGmlImportComposition.CreateConstructionSourceFactory(
                provider.GetRequiredService<ICityGmlDocumentReader>(),
                provider.GetRequiredService<IResoniteConstructionComposer>()));

        return services;
    }
}
