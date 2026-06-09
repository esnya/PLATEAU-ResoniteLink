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
        services.TryAddScoped<CreateResoniteSlot>(_ => ResoniteSlotCreator.CreateAsync);
        services.TryAddScoped<CreateTerrainTextureGenerator>(
            _ => static (httpClient, cacheRootPath, disablePersistentCache) =>
            {
                TerrainTextureAssetGenerator terrainTextureAssetGenerator = new(
                    httpClient,
                    cacheRootPath,
                    disablePersistentCache);
                return terrainTextureAssetGenerator.EnsureTextureAsync;
            });
        services.TryAddScoped<CreateNonDemCityObjectBaker>(provider =>
        {
            ResoniteTextureImageLoader textureImageLoader = provider.GetRequiredService<ResoniteTextureImageLoader>();
            return (enableMeshBake, resourceBudget, requestLocalOrigin) => CreateDefaultCityObjectBaker(
                enableMeshBake,
                resourceBudget,
                requestLocalOrigin,
                textureImageLoader);
        });
        services.TryAddScoped<CreateResoniteLiveSendRunExecutor>(
            _ => static runStarter => new ResoniteLiveSendRunExecutor(runStarter));
        services.TryAddScoped<ResoniteLiveSendRunSetupPreparer>();
        services.TryAddScoped<EnsureResoniteLiveSendConnected>(_ => ResoniteLiveSendConnectionInitializer.EnsureConnectedAsync);
        services.TryAddScoped<EnsureResoniteGsiFallbackLicense>(_ => ResoniteDatasetLicenseWriter.EnsureGsiFallbackLicenseAsync);
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

    private static NonDemCityObjectBaker? CreateDefaultCityObjectBaker(
        bool enableMeshBake,
        ResoniteImportBudgetProfile resourceBudget,
        ResoniteLocalOrigin requestLocalOrigin,
        ResoniteTextureImageLoader textureImageLoader)
    {
        _ = resourceBudget.Name switch
        {
            ResoniteImportMemoryProfile.Small or ResoniteImportMemoryProfile.Large => true,
            _ => throw new ArgumentOutOfRangeException(nameof(resourceBudget), resourceBudget.Name, "Unsupported memory profile."),
        };

        return enableMeshBake
            ? new NonDemCityObjectBaker(
                bakePolicies: NonDemCityObjectBakePolicies.DefaultPolicies,
                sourceFileBakeEmitter: CreateDefaultSourceFileBakeEmitter(
                    new NonDemAtlasBakeBudget(ResourceBudget: resourceBudget),
                    requestLocalOrigin,
                    textureImageLoader))
            : null;
    }

    private static NonDemSourceFileBakeEmitter CreateDefaultSourceFileBakeEmitter(
        NonDemAtlasBakeBudget atlasBudget,
        ResoniteLocalOrigin requestLocalOrigin,
        ResoniteTextureImageLoader textureImageLoader)
    {
        NonDemAtlasLayoutFactory layoutFactory = new(
            atlasBudget.EffectiveMaxAtlasSize,
            atlasBudget.TilePaddingPixels);
        return new NonDemSourceFileBakeEmitter(
            new NonDemCityObjectBakeCandidateFactory(
                new NonDemBakeEntryFactory(textureImageLoader, atlasBudget.EffectiveMaxAtlasTextureEdge)),
            new NonDemCityObjectBakeAssembler(
                layoutFactory,
                new NonDemAtlasImageRenderer(atlasBudget.TilePaddingPixels),
                requestLocalOrigin),
            new NonDemAtlasBatchFitPolicy(layoutFactory));
    }
}
