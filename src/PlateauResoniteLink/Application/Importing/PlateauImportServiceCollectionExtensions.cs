using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Application.Importing;

internal static class PlateauImportServiceCollectionExtensions
{
    internal static IServiceCollection AddImportedSceneSourceServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IArchiveFileLayoutPolicy, ArchiveFileLayoutPolicy>();
        services.TryAddSingleton<IRemoteArchiveDistributionPolicy, RemoteArchiveDistributionPolicy>();
        services.TryAddSingleton(CommonMaterialCatalog.Create());
        services.TryAddSingleton<Func<string, CancellationToken, Task<IPlateauDatasetContentSource>>>(provider =>
        {
            IRemoteArchiveDistributionPolicy remoteArchiveDistributionPolicy =
                provider.GetRequiredService<IRemoteArchiveDistributionPolicy>();
            IArchiveFileLayoutPolicy archiveFileLayoutPolicy =
                provider.GetRequiredService<IArchiveFileLayoutPolicy>();

            return (sourcePath, cancellationToken) => PlateauDatasetContentSourceFactory.CreateAsync(
                sourcePath,
                remoteArchiveDistributionPolicy,
                archiveFileLayoutPolicy,
                cancellationToken);
        });
        services.TryAddSingleton<Func<DatasetLocation?, CancellationToken, Task<IDemTerrainGeoReferencedRasterCatalog?>>>(provider =>
        {
            Func<string, CancellationToken, Task<IPlateauDatasetContentSource>> createDatasetContentSource =
                provider.GetRequiredService<Func<string, CancellationToken, Task<IPlateauDatasetContentSource>>>();
            return (source, cancellationToken) => DemTerrainGeoReferencedRasterCatalog.CreateAsync(
                source,
                createDatasetContentSource,
                cancellationToken);
        });
        services.TryAddSingleton<IDemTextureSourcePolicy, DefaultDemTextureSourcePolicy>();
        services.TryAddSingleton<Func<string, IPlateauDatasetContentSource, CityGmlAppearanceStore>>(
            _ => CityGmlAppearanceStore.Create);
        services.TryAddSingleton<IDefaultMaterialResolver, DefaultMaterialResolver>();
        services.TryAddSingleton<ICityGmlGeometryProjector, LocalCityGmlGeometryProjector>();
        services.TryAddSingleton<ImportedObjectUnitOptimizer>(
            _ => ImportedDynamicMaterialUvUnitOptimizer.OptimizeAsync);
        services.TryAddSingleton<ImportedSceneSourceComposer>(provider =>
        {
            DefaultImportedSceneSourceComposer composer = new(
                provider.GetRequiredService<ICityGmlGeometryProjector>(),
                provider.GetRequiredService<IDemTextureSourcePolicy>());
            return composer.Compose;
        });
        services.TryAddSingleton<ICityGmlDocumentReader>(provider =>
            new LocalCityGmlDocumentReader(
                provider.GetRequiredService<Func<string, CancellationToken, Task<IPlateauDatasetContentSource>>>(),
                provider.GetRequiredService<Func<string, IPlateauDatasetContentSource, CityGmlAppearanceStore>>()));
        services.TryAddSingleton<IImportedSceneSourceFactory>(provider =>
            new DefaultImportedSceneSourceFactory(
                provider.GetRequiredService<ICityGmlDocumentReader>(),
                provider.GetRequiredService<ImportedSceneSourceComposer>(),
                provider.GetRequiredService<ImportedObjectUnitOptimizer>()));

        return services;
    }
}
