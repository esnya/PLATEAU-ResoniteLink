using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

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
        services.TryAddSingleton<Func<DatasetLocation?, CancellationToken, Task<DemTerrainGeoReferencedRasterResolver?>>>(provider =>
        {
            Func<string, CancellationToken, Task<IPlateauDatasetContentSource>> createDatasetContentSource =
                provider.GetRequiredService<Func<string, CancellationToken, Task<IPlateauDatasetContentSource>>>();
            return (source, cancellationToken) => DemTerrainGeoReferencedRasterCatalog.CreateAsync(
                source,
                createDatasetContentSource,
                cancellationToken);
        });
        services.TryAddSingleton<ResolveDemTextureSources>(provider =>
        {
            DefaultDemTextureSourcePolicy policy = new(
                provider.GetRequiredService<Func<DatasetLocation?, CancellationToken, Task<DemTerrainGeoReferencedRasterResolver?>>>());
            return policy.ResolveAsync;
        });
        services.TryAddSingleton<Func<string, IPlateauDatasetContentSource, CityGmlAppearanceStore>>(
            _ => CityGmlAppearanceStore.Create);
        services.TryAddSingleton<ResolveDefaultMaterial>(provider =>
        {
            DefaultMaterialResolver resolver = new(
                provider.GetRequiredService<CommonMaterialCatalog<DefaultCommonMaterialMember>>());
            return resolver.ResolveMaterial;
        });
        services.TryAddSingleton<CityGmlGeometryProjector>(provider =>
        {
            ResolveDefaultMaterial materialResolver = provider.GetRequiredService<ResolveDefaultMaterial>();
            return (
                sourceFile,
                referenceSystem,
                globalOriginPoint,
                globalCartesian,
                demTerrainTextureOverlays,
                requestedMeshCodeBounds,
                selectedMeshCodes,
                request,
                predicate,
                logger,
                cancellationToken) => LocalCityGmlObjectProjection.ProjectCityObjects(
                    sourceFile,
                    referenceSystem,
                    globalOriginPoint,
                    globalCartesian,
                    demTerrainTextureOverlays,
                    requestedMeshCodeBounds,
                    selectedMeshCodes,
                    request,
                    materialResolver,
                    predicate,
                    logger,
                    cancellationToken);
        });
        services.TryAddSingleton<ImportedObjectUnitOptimizer>(
            _ => ImportedDynamicMaterialUvUnitOptimizer.OptimizeAsync);
        services.TryAddSingleton<ImportedSceneSourceComposer>(provider =>
        {
            DefaultImportedSceneSourceComposer composer = new(
                provider.GetRequiredService<CityGmlGeometryProjector>(),
                provider.GetRequiredService<ResolveDemTextureSources>());
            return composer.Compose;
        });
        services.TryAddSingleton<ReadCityGmlDocument>(provider =>
        {
            LocalCityGmlDocumentReader reader = new(
                provider.GetRequiredService<Func<string, CancellationToken, Task<IPlateauDatasetContentSource>>>(),
                provider.GetRequiredService<Func<string, IPlateauDatasetContentSource, CityGmlAppearanceStore>>());
            return reader.ReadAsync;
        });
        services.TryAddSingleton<CreateImportedSceneSource>(provider =>
        {
            ReadCityGmlDocument readCityGmlDocument = provider.GetRequiredService<ReadCityGmlDocument>();
            ImportedSceneSourceComposer constructionComposer =
                provider.GetRequiredService<ImportedSceneSourceComposer>();
            ImportedObjectUnitOptimizer objectUnitOptimizer =
                provider.GetRequiredService<ImportedObjectUnitOptimizer>();
            return async (request, loggerFactory, cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);
                ImportedSceneSourceSnapshot readResult = await readCityGmlDocument(
                    request,
                    loggerFactory?.CreateLogger("PlateauResoniteLink.Import"),
                    cancellationToken);
                return constructionComposer(request, readResult, objectUnitOptimizer, loggerFactory);
            };
        });

        return services;
    }
}
