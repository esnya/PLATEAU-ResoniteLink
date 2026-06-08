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
        services.TryAddScoped<ResoniteMaterialPlanning>();
        services.TryAddScoped<ResoniteCommonMaterialSetupPreparer>();
        services.TryAddScoped<CreateNonDemCityObjectBaker>(provider =>
        {
            ResoniteTextureImageLoader textureImageLoader = provider.GetRequiredService<ResoniteTextureImageLoader>();
            return (resourceBudget, requestLocalOrigin) =>
            {
                _ = resourceBudget.Name switch
                {
                    ResoniteImportMemoryProfile.Small or ResoniteImportMemoryProfile.Large => true,
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(resourceBudget),
                        resourceBudget.Name,
                        "Unsupported memory profile."),
                };

                NonDemAtlasBakeBudget atlasBudget = new(ResourceBudget: resourceBudget);
                NonDemAtlasLayoutFactory layoutFactory = new(
                    atlasBudget.EffectiveMaxAtlasSize,
                    atlasBudget.TilePaddingPixels);
                NonDemSourceFileBakeEmitter sourceFileBakeEmitter = new(
                    new NonDemCityObjectBakeCandidateFactory(
                        new NonDemBakeEntryFactory(textureImageLoader, atlasBudget.EffectiveMaxAtlasTextureEdge)),
                    new NonDemCityObjectBakeAssembler(
                        layoutFactory,
                        new NonDemAtlasImageRenderer(atlasBudget.TilePaddingPixels),
                        new NonDemBakedGeometryComposer(requestLocalOrigin)),
                    layoutFactory);

                return new NonDemCityObjectBaker(
                    NonDemCityObjectBakePolicies.DefaultPolicies,
                    sourceFileBakeEmitter);
            };
        });
        services.TryAddScoped<IResoniteLiveSendRunSetupPreparer, ResoniteLiveSendRunSetupPreparer>();
        services.TryAddScoped<IResoniteLiveSendRunExecutorFactory, ResoniteLiveSendRunExecutorFactory>();
        services.TryAddScoped<EnsureResoniteGsiFallbackLicense>(_ => ResoniteDatasetLicenseWriter.EnsureGsiFallbackLicenseAsync);
        services.TryAddScoped<ResonitePreparedCityObjectImporter>();
        services.TryAddScoped<CreateTerrainTextureGenerator>(_ => static (options, terrainTextureAssetHttpClient) =>
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);

            TerrainTextureAssetGenerator terrainTextureAssetGenerator = new(
                terrainTextureAssetHttpClient,
                options.TerrainTileCacheRoot,
                options.DisableTerrainTileCache);
            return terrainTextureAssetGenerator.EnsureTextureAsync;
        });
        services.TryAddScoped<Func<ResoniteLiveSceneImportTargetOptions, ResoniteLinkSendDiagnostics, ILiveSendClientSession>>(provider =>
        {
            Func<Microsoft.Extensions.Logging.ILogger, IResoniteLinkClient> baseClientFactory =
                provider.GetRequiredService<Func<Microsoft.Extensions.Logging.ILogger, IResoniteLinkClient>>();

            return (options, diagnostics) =>
            {
                ArgumentNullException.ThrowIfNull(options);
                ArgumentNullException.ThrowIfNull(diagnostics);

                Microsoft.Extensions.Logging.ILogger logger =
                    options.LoggerFactory.CreateLogger("PlateauResoniteLink.ResoniteLink");
                IResoniteLinkClient CreateConfiguredClient()
                {
                    IResoniteLinkClient client = new RetryingResoniteLinkClient(
                        () => baseClientFactory(logger),
                        logger);
                    return diagnostics.Enabled ? new MetricsResoniteLinkClient(client, diagnostics) : client;
                }

                return new LiveSendClientSession(
                    CreateConfiguredClient,
                    options.Endpoint,
                    options.ConnectionCount,
                    diagnostics,
                    logger);
            };
        });
        services.TryAddScoped<ResolveResoniteDatasetRootSlot>(_ => ResoniteSceneSlotLocator.TryGetDatasetRootAsync);
        services.TryAddScoped<ResolveResoniteSceneAnchor>(_ => ResoniteSceneAnchorResolver.ResolveAsync);
        services.TryAddScoped<CreateResoniteSlot>(_ => ResoniteSlotCreator.CreateAsync);
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
