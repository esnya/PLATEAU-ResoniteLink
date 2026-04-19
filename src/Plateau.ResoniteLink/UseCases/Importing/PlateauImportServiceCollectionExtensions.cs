using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Plateau.ResoniteLink.Application.Importing;

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
        services.TryAddSingleton<IResoniteConstructionComposer, LocalCityGmlConstructionComposer>();
        services.TryAddSingleton<ICityGmlDocumentReader, LocalCityGmlDocumentReader>();
        services.TryAddSingleton<IResoniteConstructionSourceFactory>(
            static provider => new LocalCityGmlConstructionSourceFactory(
                provider.GetRequiredService<ICityGmlDocumentReader>(),
                provider.GetRequiredService<IResoniteConstructionComposer>()));

        return services;
    }
}
