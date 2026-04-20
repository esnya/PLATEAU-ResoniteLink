using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PlateauResoniteLink.Application.Importing;

public static class PlateauImportServiceCollectionExtensions
{
    public static IServiceCollection AddPlateauCityGmlImportServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IArchiveFileLayoutPolicy, ArchiveFileLayoutPolicy>();
        services.TryAddSingleton<IRemoteArchiveDistributionPolicy, RemoteArchiveDistributionPolicy>();
        services.TryAddSingleton<IPlateauDatasetContentSourceFactory, DefaultPlateauDatasetContentSourceFactory>();
        services.TryAddSingleton<ICityGmlAppearanceStoreFactory, CityGmlAppearanceStoreFactory>();
        services.TryAddSingleton<ICityGmlLodSelector, CityGmlLodSelector>();
        services.TryAddSingleton<IDefaultMaterialResolver, DefaultMaterialResolver>();
        services.TryAddSingleton<ICityGmlGeometryProjector, LocalCityGmlGeometryProjector>();
        services.TryAddSingleton<ICityGmlCommonMaterialEnumerator, LocalCityGmlCommonMaterialEnumerator>();
        services.TryAddSingleton<IImportedSceneSourceComposer>(provider =>
            new LocalCityGmlConstructionComposer(
                provider.GetRequiredService<ICityGmlGeometryProjector>(),
                provider.GetRequiredService<ICityGmlCommonMaterialEnumerator>()));
        services.TryAddSingleton<ICityGmlDocumentReader>(provider =>
            new LocalCityGmlDocumentReader(
                provider.GetRequiredService<IPlateauDatasetContentSourceFactory>(),
                provider.GetRequiredService<ICityGmlAppearanceStoreFactory>(),
                provider.GetRequiredService<ICityGmlLodSelector>()));
        services.TryAddSingleton<IImportedSceneSourceFactory>(provider =>
            new LocalCityGmlConstructionSourceFactory(
                provider.GetRequiredService<ICityGmlDocumentReader>(),
                provider.GetRequiredService<IImportedSceneSourceComposer>()));

        return services;
    }
}
