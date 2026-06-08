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

        services.TryAddScoped<Func<Microsoft.Extensions.Logging.ILogger, IResoniteLinkClient>>(
            _ => static logger => new ResoniteLinkClient(logger));
        services.TryAddScoped<BundledDefaultMaterialAssetStore>();
        services.TryAddScoped<ResoniteTextureImageLoader>();
        services.TryAddScoped<NonDemSourceFileBakeEmitterFactory>();
        services.TryAddScoped<ResoniteMaterialPlanning>();
        services.TryAddScoped<ResoniteSceneMaterialPlanComposer>();
        services.TryAddScoped<ResoniteCommonMaterialSetupPreparer>();
        services.TryAddScoped<IResoniteLiveSendRunSetupPreparer, ResoniteLiveSendRunSetupPreparer>();
        services.TryAddScoped<IResoniteLiveSendRunExecutorFactory, ResoniteLiveSendRunExecutorFactory>();
        services.TryAddScoped<ResonitePreparedCityObjectImporter>();
        services.TryAddScoped<Func<ResoniteLiveSceneImportTargetOptions, ResoniteLinkSendDiagnostics, ILiveSendClientSession>>(provider =>
        {
            Func<Action<string>?, IResoniteLinkClient> baseClientFactory =
                provider.GetRequiredService<Func<Action<string>?, IResoniteLinkClient>>();

            return (options, diagnostics) =>
            {
                ArgumentNullException.ThrowIfNull(options);
                ArgumentNullException.ThrowIfNull(diagnostics);

                return ResoniteLinkTransportSessionFactory.Create(
                    options.Endpoint,
                    options.ConnectionCount,
                    diagnostics,
                    options.ProgressReporter,
                    baseClientFactory);
            };
        });
        services.TryAddScoped<IResoniteSceneSetupInterpreter, ResoniteSceneSetupInterpreter>();
        services.TryAddScoped<ResoniteLiveSceneImportFactory>();

        return services;
    }
}
