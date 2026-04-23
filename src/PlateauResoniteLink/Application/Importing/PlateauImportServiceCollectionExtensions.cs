using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PlateauResoniteLink.Application.Importing;

internal static class PlateauImportServiceCollectionExtensions
{
    internal static IServiceCollection AddLocalCityGmlImportServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IArchiveFileLayoutPolicy, ArchiveFileLayoutPolicy>();
        services.TryAddSingleton<IRemoteArchiveDistributionPolicy, RemoteArchiveDistributionPolicy>();
        services.TryAddSingleton<CommonMaterialCatalog>();
        services.TryAddSingleton<IPlateauDatasetContentSourceFactory, DefaultPlateauDatasetContentSourceFactory>();
        services.TryAddSingleton<IDemTerrainGeoReferencedRasterCatalogFactory, DefaultDemTerrainGeoReferencedRasterCatalogFactory>();
        services.TryAddSingleton<IDemTextureSourcePolicy, LocalCityGmlDemTextureSourcePolicy>();
        services.TryAddSingleton<IImportedCityObjectOptimizer, PassthroughImportedCityObjectOptimizer>();
        services.TryAddSingleton<ICityGmlAppearanceStoreFactory, CityGmlAppearanceStoreFactory>();
        services.TryAddSingleton<ICityGmlLodSelector, CityGmlLodSelector>();
        services.TryAddSingleton<IDefaultMaterialResolver, DefaultMaterialResolver>();
        services.TryAddSingleton<ICityGmlGeometryProjector, LocalCityGmlGeometryProjector>();
        services.TryAddSingleton<IImportedSceneSourceComposer>(provider =>
            new LocalCityGmlConstructionComposer(
                provider.GetRequiredService<ICityGmlGeometryProjector>(),
                provider.GetRequiredService<IDemTextureSourcePolicy>()));
        services.TryAddSingleton<ICityGmlDocumentReader>(provider =>
            new LocalCityGmlDocumentReader(
                provider.GetRequiredService<IPlateauDatasetContentSourceFactory>(),
                provider.GetRequiredService<ICityGmlAppearanceStoreFactory>(),
                provider.GetRequiredService<ICityGmlLodSelector>()));
        services.TryAddSingleton<IImportedSceneSourceFactory>(provider =>
            new LocalCityGmlConstructionSourceFactory(
                provider.GetRequiredService<ICityGmlDocumentReader>(),
                provider.GetRequiredService<IImportedSceneSourceComposer>(),
                provider.GetRequiredService<IDemTextureSourcePolicy>(),
                provider.GetRequiredService<IImportedCityObjectOptimizer>()));

        return services;
    }
}
