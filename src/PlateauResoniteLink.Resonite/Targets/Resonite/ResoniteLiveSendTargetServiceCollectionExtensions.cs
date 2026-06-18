using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using PlateauResoniteLink.Resonite.Targets.Resonite.Execution;
using PlateauResoniteLink.Resonite.Transport.ResoniteLink;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

public static class ResoniteLiveSendTargetServiceCollectionExtensions
{
    public static IServiceCollection AddResoniteLiveSendTargetServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddResoniteTargetPipelineServices();
        services.TryAddScoped<Func<IResoniteLinkClient>>(
            _ => static () => new ResoniteLinkClient());
        services.TryAddScoped<IResoniteClientSessionFactory, ResoniteLinkClientSessionFactory>();
        services.TryAddScoped<IResoniteLiveSceneImportFactory, ResoniteLiveSceneImportFactory>();

        return services;
    }

    internal static IServiceCollection AddResoniteTargetPipelineServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<BundledDefaultMaterialAssetStore>();
        services.TryAddScoped<ResoniteTextureImageLoader>();
        services.TryAddScoped<INonDemSourceFileBakeEmitterFactory, NonDemSourceFileBakeEmitterFactory>();
        services.TryAddScoped<ResoniteBufferedCityObjectBakerFactory>();
        services.TryAddScoped<IResoniteMaterialPlanning, ResoniteMaterialPlanning>();
        services.TryAddScoped<ResoniteSceneMaterialPlanComposer>();
        services.TryAddScoped<ResoniteCommonMaterialSetupPreparer>();
        services.TryAddScoped<ResonitePreparedRunSetupComposer>();
        services.TryAddScoped<IResoniteLiveSendRunSetupPreparer, ResoniteLiveSendRunSetupPreparer>();
        services.TryAddScoped<LiveSendRunStateFactory>();
        services.TryAddScoped<ResoniteLiveSendRunStarterFactory>();
        services.TryAddScoped<IResoniteLiveSendRunExecutorFactory, ResoniteLiveSendRunExecutorFactory>();
        services.TryAddScoped<IResoniteLiveSendWorkerPipelineFactory, ResoniteLiveSendWorkerPipelineFactory>();
        services.TryAddScoped<ResoniteLiveSendWorkerLauncherFactory>();
        services.TryAddScoped<ResonitePreparedCityObjectImporter>();
        services.TryAddScoped<IResoniteSlotCreator, ResoniteSlotCreator>();
        services.TryAddScoped<IResoniteSceneAnchorResolver, ResoniteSceneAnchorResolver>();
        services.TryAddScoped<IResoniteSceneSlotLocator, ResoniteSceneSlotLocator>();
        services.TryAddScoped<IResoniteSceneSetupObserver, ResoniteSceneSetupObserver>();
        services.TryAddScoped<IResoniteSceneSetupInterpreter>(
            static serviceProvider => new ResoniteSceneSetupInterpreter(
                serviceProvider.GetRequiredService<IResoniteSceneSetupObserver>(),
                serviceProvider.GetRequiredService<IResoniteSceneAnchorResolver>()));
        services.TryAddScoped<ResoniteLiveSceneImportDependencyFactory>();

        return services;
    }
}
