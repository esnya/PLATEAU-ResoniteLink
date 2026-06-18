using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using PlateauResoniteLink.Application.Importing.CityGml;
using PlateauResoniteLink.Application.Importing.Contracts;
using PlateauResoniteLink.Application.Importing.Plateau;
using PlateauResoniteLink.Application.Importing.Source;
using PlateauResoniteLink.Plateau.TerrainTextures;

using PlateauResoniteLink.Core;

namespace PlateauResoniteLink.Application.Importing;

public static class PlateauImportServiceCollectionExtensions
{
    public static IServiceCollection AddImportedSceneSourceServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IArchiveFileLayoutPolicy, ArchiveFileLayoutPolicy>();
        services.TryAddSingleton<IRemoteArchiveDistributionPolicy, RemoteArchiveDistributionPolicy>();
        services.TryAddSingleton(CommonMaterialCatalog.Create());
        services.TryAddSingleton<IPlateauDatasetContentSourceFactory, DefaultPlateauDatasetContentSourceFactory>();
        services.TryAddSingleton(provider =>
            new DatasetInspectionService(provider.GetRequiredService<IPlateauDatasetContentSourceFactory>()));
        services.TryAddSingleton<IDemTerrainGeoReferencedRasterCatalogFactory, DefaultDemTerrainGeoReferencedRasterCatalogFactory>();
        services.TryAddSingleton<IDemTextureSourcePolicy, DefaultDemTextureSourcePolicy>();
        services.TryAddSingleton<ICityGmlAppearanceStoreFactory, CityGmlAppearanceStoreFactory>();
        services.TryAddSingleton<ICityGmlLodSelector, CityGmlLodSelector>();
        services.TryAddSingleton<IDefaultMaterialResolver, DefaultMaterialResolver>();
        services.TryAddSingleton<ICityGmlGeometryProjector, LocalCityGmlGeometryProjector>();
        services.TryAddSingleton<IImportedSceneMetadataComposer, DefaultImportedSceneMetadataComposer>();
        services.TryAddSingleton<ImportedDynamicMaterialUvUnitOptimizer>();
        services.TryAddSingleton<IImportedObjectUnitOptimizer>(provider =>
            new CompositeImportedObjectUnitOptimizer(
                [
                    provider.GetRequiredService<ImportedDynamicMaterialUvUnitOptimizer>(),
                ]));
        services.TryAddSingleton<IImportedSceneSourceComposer>(provider =>
            new DefaultImportedSceneSourceComposer(
                provider.GetRequiredService<IImportedSceneMetadataComposer>(),
                provider.GetRequiredService<ICityGmlGeometryProjector>(),
                provider.GetRequiredService<IDemTextureSourcePolicy>()));
        services.TryAddSingleton<ICityGmlDocumentReader>(provider =>
            new LocalCityGmlDocumentReader(
                provider.GetRequiredService<IPlateauDatasetContentSourceFactory>(),
                provider.GetRequiredService<ICityGmlAppearanceStoreFactory>(),
                provider.GetRequiredService<ICityGmlLodSelector>()));
        services.TryAddSingleton<IResolvedPlateauSceneSourceReader, CityGmlResolvedPlateauSceneSourceReader>();
        services.TryAddScoped<ITerrainTextureSourceImageReaderFactory, TerrainTextureSourceImageReaderFactory>();
        services.TryAddScoped<ITerrainTextureAssetGeneratorFactory, TerrainTextureAssetGeneratorFactory>();
        services.TryAddSingleton<IImportedSceneSourceFactory>(provider =>
            new DefaultImportedSceneSourceFactory(
                provider.GetRequiredService<IResolvedPlateauSceneSourceReader>(),
                provider.GetRequiredService<IImportedSceneSourceComposer>(),
                provider.GetRequiredService<IImportedObjectUnitOptimizer>()));

        return services;
    }
}
