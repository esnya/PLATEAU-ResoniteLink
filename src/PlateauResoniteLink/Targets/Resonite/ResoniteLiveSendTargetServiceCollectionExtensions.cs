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
        services.TryAddScoped<ResoniteMaterialPlanning>();
        services.TryAddScoped<ResoniteCommonMaterialSetupPreparer>();
        services.TryAddScoped<ResoniteLiveSendRunSetupPreparer>();
        services.TryAddScoped<EnsureResoniteLiveSendConnected>(_ => ResoniteLiveSendConnectionInitializer.EnsureConnectedAsync);
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
        services.TryAddScoped<ResolveResoniteDatasetRootSlot>(_ => ResoniteSceneSlotLocator.TryGetDatasetRootAsync);
        services.TryAddScoped<ResolveResoniteSceneAnchor>(_ => ResoniteSceneAnchorResolver.ResolveAsync);
        services.TryAddScoped<SetupResoniteScene>(provider =>
        {
            ResolveResoniteDatasetRootSlot resolveDatasetRootSlot = provider.GetRequiredService<ResolveResoniteDatasetRootSlot>();
            ResolveResoniteSceneAnchor resolveSceneAnchor = provider.GetRequiredService<ResolveResoniteSceneAnchor>();
            return (setupClient, setupInfo, commonMaterials, cancellationToken) => ResoniteSceneSetupInterpreter.SetupAsync(
                setupClient,
                setupInfo,
                commonMaterials,
                resolveDatasetRootSlot,
                resolveSceneAnchor,
                cancellationToken);
        });
        services.TryAddScoped<ResoniteLiveSceneImportFactory>();

        return services;
    }
}
