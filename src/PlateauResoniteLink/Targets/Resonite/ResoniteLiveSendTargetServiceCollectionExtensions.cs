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

        services.TryAddScoped<Func<Microsoft.Extensions.Logging.ILogger, IResoniteLinkClient>>(
            _ => static logger => new ResoniteLinkClient(logger));
        services.TryAddScoped<BundledDefaultMaterialAssetStore>();
        services.TryAddScoped<ResoniteTextureImageLoader>();
        services.TryAddScoped<INonDemSourceFileBakeEmitterFactory, NonDemSourceFileBakeEmitterFactory>();
        services.TryAddScoped<IResoniteBufferedCityObjectBakerFactory, ResoniteBufferedCityObjectBakerFactory>();
        services.TryAddScoped<IResoniteMaterialPlanning, ResoniteMaterialPlanning>();
        services.TryAddScoped<ResoniteSceneMaterialPlanComposer>();
        services.TryAddScoped<ResoniteCommonMaterialSetupPreparer>();
        services.TryAddScoped<ResonitePreparedRunSetupComposer>();
        services.TryAddScoped<ResoniteLiveSendRunSetupPreparer>();
        services.TryAddScoped<LiveSendRunStateFactory>();
        services.TryAddScoped<ResoniteLiveSendRunStarterFactory>();
        services.TryAddScoped<IResoniteLiveSendWorkerPipelineFactory, ResoniteLiveSendWorkerPipelineFactory>();
        services.TryAddScoped<ResoniteLiveSendWorkerLauncherFactory>();
        services.TryAddScoped<ResonitePreparedCityObjectImporter>();
        services.TryAddScoped<IResoniteCanonicalSceneDumpSinkFactory, ResoniteCanonicalSceneDumpSinkFactory>();
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
        services.TryAddScoped<ResoniteLiveSceneImportDependencyFactory>();
        services.TryAddScoped<IResoniteLiveSceneImportFactory, ResoniteLiveSceneImportFactory>();

        return services;
    }
}
