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
        services.TryAddScoped<NonDemSourceFileBakeEmitterFactory>();
        services.TryAddScoped<ResoniteBufferedCityObjectBakerFactory>();
        services.TryAddScoped<ResoniteMaterialPlanning>();
        services.TryAddScoped<ResoniteSceneMaterialPlanComposer>();
        services.TryAddScoped<ResoniteCommonMaterialSetupPreparer>();
        services.TryAddScoped<IResoniteLiveSendRunSetupPreparer, ResoniteLiveSendRunSetupPreparer>();
        services.TryAddScoped<LiveSendRunStateFactory>();
        services.TryAddScoped<ResoniteLiveSendRunStarterFactory>();
        services.TryAddScoped<IResoniteLiveSendRunExecutorFactory, ResoniteLiveSendRunExecutorFactory>();
        services.TryAddScoped<ResoniteLiveSendWorkerPipelineFactory>();
        services.TryAddScoped<ResonitePreparedCityObjectImporter>();
        services.TryAddScoped<ResoniteCanonicalSceneDumpSinkFactory>();
        services.TryAddScoped<IResoniteClientSessionFactory, ResoniteLinkClientSessionFactory>();
        services.TryAddScoped<IResoniteSceneSetupInterpreter, ResoniteSceneSetupInterpreter>();
        services.TryAddScoped<IResoniteLiveSceneImportFactory, ResoniteLiveSceneImportFactory>();

        return services;
    }
}
