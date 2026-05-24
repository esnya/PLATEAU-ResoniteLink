using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

public static class ResoniteLiveSendTargetServiceCollectionExtensions
{
    public static IServiceCollection AddResoniteLiveSendTargetServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<Func<Action<string>?, IResoniteLinkClient>>(
            _ => static progressReporter => new ResoniteLinkClient(progressReporter));
        services.TryAddScoped<BundledDefaultMaterialAssetStore>();
        services.TryAddScoped<IResoniteBatchEmissionPlanner, ResoniteBatchEmissionPlanner>();
        services.TryAddScoped<IResoniteBufferedCityObjectBakerFactory, ResoniteBufferedCityObjectBakerFactory>();
        services.TryAddScoped<IResonitePreparedGeometryFactory, ResonitePreparedGeometryFactory>();
        services.TryAddScoped<IResoniteGeometryAssetAssembler, ResoniteGeometryAssetAssembler>();
        services.TryAddScoped<IResoniteGeometryAssetPlanner, ResoniteGeometryAssetPlanner>();
        services.TryAddScoped<IResoniteMaterialPlanning, ResoniteMaterialPlanning>();
        services.TryAddScoped<IResoniteSceneMaterialPlanFactory, ResoniteSceneMaterialPlanFactory>();
        services.TryAddScoped<IResoniteSharedTerrainTextureAssetStore, ResoniteSharedTerrainTextureAssetStore>();
        services.TryAddScoped<IResonitePreparedTextureUploader, ResonitePreparedTextureUploader>();
        services.TryAddScoped<IResonitePreparedCityObjectImporter, ResonitePreparedCityObjectImporter>();
        services.TryAddScoped<IResonitePreparedTextureReferenceFactoryFactory, ResonitePreparedTextureReferenceFactoryFactory>();
        services.TryAddScoped<IResonitePreparedCityObjectFactoryFactory, ResonitePreparedCityObjectFactoryFactory>();
        services.TryAddScoped<IResoniteQueuedCityObjectSenderFactory, ResoniteQueuedCityObjectSenderFactory>();
        services.TryAddScoped<IResoniteLiveSendRunFinalizer, ResoniteLiveSendRunFinalizer>();
        services.TryAddScoped<IResoniteLiveSendExecutionResultFactory, ResoniteLiveSendExecutionResultFactory>();
        services.TryAddScoped<IResoniteLiveSendRunResourceReleaser, ResoniteLiveSendRunResourceReleaser>();
        services.TryAddScoped<IResoniteLiveSendExecutionGateFactory, ResoniteLiveSendExecutionGateFactory>();
        services.TryAddScoped<IResoniteLiveSendRunStarter, ResoniteLiveSendRunStarter>();
        services.TryAddScoped<IResoniteLiveSendConnectionInitializer, ResoniteLiveSendConnectionInitializer>();
        services.TryAddScoped<IResoniteCityObjectQueueWriter, ResoniteCityObjectQueueWriter>();
        services.TryAddScoped<IResoniteImportedObjectUnitStreamQueueWriter, ResoniteImportedObjectUnitStreamQueueWriter>();
        services.TryAddScoped<IResoniteCityObjectSendWorkerPool, ResoniteCityObjectSendWorkerPool>();
        services.TryAddScoped<IResoniteLiveSendWorkerLauncher, ResoniteLiveSendWorkerLauncher>();
        services.TryAddScoped<ILiveSendRunPlanFactory, LiveSendRunPlanFactory>();
        services.TryAddScoped<IResoniteCommonMaterialSetupAssetPreparer, ResoniteCommonMaterialSetupAssetPreparer>();
        services.TryAddScoped<IResoniteSceneBatchEmitter, PlannedBatchEmissionInterpreter>();
        services.TryAddScoped<IResoniteSlotCreator, ResoniteSlotCreator>();
        services.TryAddScoped<IResoniteSharedSlotIndexFactory, ResoniteSharedSlotIndexFactory>();
        services.TryAddScoped<IResoniteLiveSendSceneSetupRunner, ResoniteLiveSendSceneSetupRunner>();
        services.TryAddScoped<IResoniteSceneAnchorResolver, ResoniteSceneAnchorResolver>();
        services.TryAddScoped<IResoniteSceneSlotLocator, ResoniteSceneSlotLocator>();
        services.TryAddScoped<IResoniteClientSessionFactory, ResoniteLinkClientSessionFactory>();
        services.TryAddScoped<ILiveSendRunStateFactory, LiveSendRunStateFactory>();
        services.TryAddScoped<IResoniteTextureImageLoaderFactory, ResoniteTextureImageLoaderFactory>();
        services.TryAddScoped<ITerrainTextureAssetGeneratorFactory, TerrainTextureAssetGeneratorFactory>();
        services.TryAddScoped<IResoniteSceneSetupInterpreter>(
            static serviceProvider => new ResoniteSceneSetupInterpreter(
                serviceProvider.GetRequiredService<IResoniteSceneSlotLocator>(),
                serviceProvider.GetRequiredService<IResoniteSceneAnchorResolver>()));
        services.TryAddScoped<IResoniteDatasetLicenseWriter, ResoniteDatasetLicenseWriter>();
        services.TryAddScoped<IResoniteLiveSceneImportDependencyFactory, ResoniteLiveSceneImportDependencyFactory>();
        services.TryAddScoped<IResoniteLiveSceneImportFactory, ResoniteLiveSceneImportFactory>();

        return services;
    }
}
