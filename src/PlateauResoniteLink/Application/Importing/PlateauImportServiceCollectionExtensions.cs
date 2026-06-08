using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PlateauResoniteLink.Application.Importing;

internal static class PlateauImportServiceCollectionExtensions
{
    internal static IServiceCollection AddImportedSceneSourceServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IArchiveFileLayoutPolicy, ArchiveFileLayoutPolicy>();
        services.TryAddSingleton<IRemoteArchiveDistributionPolicy, RemoteArchiveDistributionPolicy>();
        services.TryAddSingleton(CommonMaterialCatalog.Create());
        services.TryAddSingleton<IPlateauDatasetContentSourceFactory, DefaultPlateauDatasetContentSourceFactory>();
        services.TryAddSingleton<IDemTerrainGeoReferencedRasterCatalogFactory, DefaultDemTerrainGeoReferencedRasterCatalogFactory>();
        services.TryAddSingleton<IDemTextureSourcePolicy, DefaultDemTextureSourcePolicy>();
        services.TryAddSingleton<Func<string, IPlateauDatasetContentSource, ICityGmlAppearanceStore>>(
            _ => CityGmlAppearanceStore.Create);
        services.TryAddSingleton<ICityGmlLodSelector, CityGmlLodSelector>();
        services.TryAddSingleton<IDefaultMaterialResolver, DefaultMaterialResolver>();
        services.TryAddSingleton<ICityGmlGeometryProjector, LocalCityGmlGeometryProjector>();
        services.TryAddSingleton<ImportedDynamicMaterialUvUnitOptimizer>();
        services.TryAddSingleton<IImportedObjectUnitOptimizer>(provider =>
            new CompositeImportedObjectUnitOptimizer(
                [
                    provider.GetRequiredService<ImportedDynamicMaterialUvUnitOptimizer>(),
                ]));
        services.TryAddSingleton<IImportedSceneSourceComposer>(provider =>
            new DefaultImportedSceneSourceComposer(
                provider.GetRequiredService<ICityGmlGeometryProjector>(),
                provider.GetRequiredService<IDemTextureSourcePolicy>()));
        services.TryAddSingleton<ICityGmlDocumentReader>(provider =>
            new LocalCityGmlDocumentReader(
                provider.GetRequiredService<IPlateauDatasetContentSourceFactory>(),
                provider.GetRequiredService<Func<string, IPlateauDatasetContentSource, ICityGmlAppearanceStore>>(),
                provider.GetRequiredService<ICityGmlLodSelector>()));
        services.TryAddSingleton<IImportedSceneSourceFactory>(provider =>
            new DefaultImportedSceneSourceFactory(
                provider.GetRequiredService<ICityGmlDocumentReader>(),
                provider.GetRequiredService<IImportedSceneSourceComposer>(),
                provider.GetRequiredService<IImportedObjectUnitOptimizer>()));

        return services;
    }
}
