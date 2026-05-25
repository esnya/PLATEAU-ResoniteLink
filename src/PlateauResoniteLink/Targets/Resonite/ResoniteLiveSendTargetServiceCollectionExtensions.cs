using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Targets.Resonite.Diagnostics;
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
        services.TryAddScoped<IResoniteCommonMaterialSetupPreparer, ResoniteCommonMaterialSetupPreparer>();
        services.TryAddScoped<ILiveSendRunPlanFactory, LiveSendRunPlanFactory>();
        services.TryAddScoped<IResoniteLiveSendConnectionInitializer, ResoniteLiveSendConnectionInitializer>();
        services.TryAddScoped<IResonitePreparedRunSetupComposer, ResonitePreparedRunSetupComposer>();
        services.TryAddScoped<IResoniteLiveSendRunSetupPreparer, ResoniteLiveSendRunSetupPreparer>();
        services.TryAddScoped<ILiveSendRunStateFactory, LiveSendRunStateFactory>();
        services.TryAddScoped<IResoniteLiveSendRunStarterFactory, ResoniteLiveSendRunStarterFactory>();
        services.TryAddScoped<IResoniteLiveSendWorkerPipelineFactory, ResoniteLiveSendWorkerPipelineFactory>();
        services.TryAddScoped<IResoniteLiveSendWorkerLauncherFactory, ResoniteLiveSendWorkerLauncherFactory>();
        services.TryAddScoped<IResoniteLiveSendStartRequestFactory, ResoniteLiveSendStartRequestFactory>();
        services.TryAddScoped<IResonitePreparedCityObjectImporter, ResonitePreparedCityObjectImporter>();
        services.TryAddScoped<IResoniteQueuedCityObjectEnqueuer, ResoniteQueuedCityObjectEnqueuer>();
        services.TryAddScoped<IResoniteLiveSendFinalizer, ResoniteLiveSendFinalizer>();
        services.TryAddScoped<IResoniteLiveSendQueue, ResoniteLiveSendQueue>();
        services.TryAddScoped<IResoniteLiveSendRunResourceReleaser, ResoniteLiveSendRunResourceReleaser>();
        services.TryAddScoped<IResoniteLiveSendRunExecutorFactory, ResoniteLiveSendRunExecutorFactory>();
        services.TryAddScoped<IResoniteCanonicalSceneDumpSinkFactory, ResoniteCanonicalSceneDumpSinkFactory>();
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
