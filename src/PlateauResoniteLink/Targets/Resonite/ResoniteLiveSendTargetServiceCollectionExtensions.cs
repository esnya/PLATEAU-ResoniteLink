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
        services.TryAddScoped<ResoniteTextureImageLoader>();
        services.TryAddScoped<INonDemSourceFileBakeEmitterFactory, NonDemSourceFileBakeEmitterFactory>();
        services.TryAddScoped<IResoniteBatchEmissionPlanner, ResoniteBatchEmissionPlanner>();
        services.TryAddScoped<IResoniteBufferedCityObjectBakerFactory, ResoniteBufferedCityObjectBakerFactory>();
        services.TryAddScoped<IResoniteGeometryAssetAssembler, ResoniteGeometryAssetAssembler>();
        services.TryAddScoped<IResoniteGeometryAssetPlanner, ResoniteGeometryAssetPlanner>();
        services.TryAddScoped<IResoniteMaterialPlanning, ResoniteMaterialPlanning>();
        services.TryAddScoped<IResoniteSceneMaterialPlanComposer, ResoniteSceneMaterialPlanComposer>();
        services.TryAddScoped<IResoniteLiveSendConnectionInitializer, ResoniteLiveSendConnectionInitializer>();
        services.TryAddScoped<IResoniteLiveSendSetupInitializer, ResoniteLiveSendSetupInitializer>();
        services.TryAddScoped<IResoniteCommonMaterialSetupPreparer, ResoniteCommonMaterialSetupPreparer>();
        services.TryAddScoped<IResoniteCommonMaterialSetupCachePrimer, ResoniteCommonMaterialSetupCachePrimer>();
        services.TryAddScoped<ILiveSendRunPlanFactory, LiveSendRunPlanFactory>();
        services.TryAddScoped<IResoniteLiveSendRunPlanInitializer, ResoniteLiveSendRunPlanInitializer>();
        services.TryAddScoped<ILiveSendRunStateFactory, LiveSendRunStateFactory>();
        services.TryAddScoped<IResoniteLiveSendRunActivatorFactory, ResoniteLiveSendRunActivatorFactory>();
        services.TryAddScoped<IResoniteLiveSendContextFactory, ResoniteLiveSendContextFactory>();
        services.TryAddScoped<IResoniteLiveSendResourceReleaser, ResoniteLiveSendResourceReleaser>();
        services.TryAddScoped<IResoniteSharedSlotIndexFactory, ResoniteSharedSlotIndexFactory>();
        services.TryAddScoped<IResoniteLiveSendRunStarterFactory, ResoniteLiveSendRunStarterFactory>();
        services.TryAddScoped<IResoniteLiveSendWorkerLauncherFactory, ResoniteLiveSendWorkerLauncherFactory>();
        services.TryAddScoped<IResoniteLiveSendStartRequestFactory, ResoniteLiveSendStartRequestFactory>();
        services.TryAddScoped<IResoniteSharedTerrainTextureAssetWriter, ResoniteSharedTerrainTextureAssetWriter>();
        services.TryAddScoped<IResonitePreparedTextureUploader, ResonitePreparedTextureUploader>();
        services.TryAddScoped<IResonitePreparedCityObjectAssetPlanner, ResonitePreparedCityObjectAssetPlanner>();
        services.TryAddScoped<IResonitePreparedCityObjectImporter, ResonitePreparedCityObjectImporter>();
        services.TryAddScoped<IResoniteQueuedGeometryPreparer, ResoniteQueuedGeometryPreparer>();
        services.TryAddScoped<IResoniteQueuedSendFailurePolicy, ResoniteQueuedSendFailurePolicy>();
        services.TryAddScoped<IResoniteQueuedCityObjectPreparerFactory, ResoniteQueuedCityObjectPreparerFactory>();
        services.TryAddScoped<IResoniteQueuedCityObjectSenderFactory, ResoniteQueuedCityObjectSenderFactory>();
        services.TryAddScoped<IResoniteQueuedCityObjectLaneProcessorFactory, ResoniteQueuedCityObjectLaneProcessorFactory>();
        services.TryAddScoped<IResoniteQueuedCityObjectEnqueuer, ResoniteQueuedCityObjectEnqueuer>();
        services.TryAddScoped<IResoniteLiveSendFinalizer, ResoniteLiveSendFinalizer>();
        services.TryAddScoped<IResoniteLiveSendQueue, ResoniteLiveSendQueue>();
        services.TryAddScoped<IResoniteSceneBatchEmitter, PlannedBatchEmissionInterpreter>();
        services.TryAddScoped<IResoniteSlotCreator, ResoniteSlotCreator>();
        services.TryAddScoped<IResoniteSceneAnchorResolver, ResoniteSceneAnchorResolver>();
        services.TryAddScoped<IResoniteSceneSlotLocator, ResoniteSceneSlotLocator>();
        services.TryAddScoped<IResoniteClientSessionFactory, ResoniteLinkClientSessionFactory>();
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
