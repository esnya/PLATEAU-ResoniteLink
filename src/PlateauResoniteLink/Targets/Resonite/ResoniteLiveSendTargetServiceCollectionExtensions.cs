using System;
using System.Net.Http;

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
        services.TryAddScoped<IResoniteBatchEmissionPlanner, ResoniteBatchEmissionPlanner>();
        services.TryAddScoped<IResoniteBufferedCityObjectBakerFactory, ResoniteBufferedCityObjectBakerFactory>();
        services.TryAddScoped<IResoniteGeometryAssetAssembler, ResoniteGeometryAssetAssembler>();
        services.TryAddScoped<IResoniteGeometryAssetPlanner, ResoniteGeometryAssetPlanner>();
        services.TryAddScoped<IResoniteMaterialPlanning, ResoniteMaterialPlanning>();
        services.TryAddScoped<IResoniteSceneMaterialPlanComposer, ResoniteSceneMaterialPlanComposer>();
        services.TryAddScoped<ILiveSendRunPlanFactory, LiveSendRunPlanFactory>();
        services.TryAddScoped<ILiveSendRunStateFactory, LiveSendRunStateFactory>();
        services.TryAddScoped<IResoniteLiveSendStartRequestFactory, ResoniteLiveSendStartRequestFactory>();
        services.TryAddScoped<IResonitePreparedCityObjectImporter, ResonitePreparedCityObjectImporter>();
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

internal interface IResoniteLiveSceneImportFactory
{
    ResoniteLiveSceneImportTarget CreateTarget(
        ResoniteLiveSceneImportTargetOptions options,
        HttpClient terrainTextureAssetHttpClient);
}

internal sealed class ResoniteLiveSceneImportFactory(
    IResoniteLiveSceneImportDependencyFactory dependencyFactory) : IResoniteLiveSceneImportFactory
{
    public ResoniteLiveSceneImportTarget CreateTarget(
        ResoniteLiveSceneImportTargetOptions options,
        HttpClient terrainTextureAssetHttpClient)
    {
        ResoniteLiveSceneImportDependencies dependencies = dependencyFactory.Create(
            options,
            terrainTextureAssetHttpClient);
        return new ResoniteLiveSceneImportTarget(options, dependencies);
    }
}

internal interface IResoniteLiveSceneImportDependencyFactory
{
    ResoniteLiveSceneImportDependencies Create(
        ResoniteLiveSceneImportTargetOptions options,
        HttpClient terrainTextureAssetHttpClient);
}

internal interface IResoniteClientSessionFactory
{
    ILiveSendClientSession Create(
        ResoniteLiveSceneImportTargetOptions options,
        ResoniteLinkSendDiagnostics diagnostics);
}

internal sealed class ResoniteLinkClientSessionFactory(
    Func<Action<string>?, IResoniteLinkClient> baseClientFactory) : IResoniteClientSessionFactory
{
    public ILiveSendClientSession Create(
        ResoniteLiveSceneImportTargetOptions options,
        ResoniteLinkSendDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(diagnostics);

        return ResoniteLinkTransportSessionFactory.Create(
            options.Endpoint,
            options.ConnectionCount,
            diagnostics,
            options.ProgressReporter,
            baseClientFactory);
    }
}

internal interface ITerrainTextureAssetGeneratorFactory
{
    ITerrainTextureAssetGenerator Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options);
}

internal sealed class TerrainTextureAssetGeneratorFactory : ITerrainTextureAssetGeneratorFactory
{
    public ITerrainTextureAssetGenerator Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);
        ArgumentNullException.ThrowIfNull(options);
        return new TerrainTextureAssetGenerator(
            terrainTextureAssetHttpClient,
            options.TerrainTileCacheRoot,
            options.DisableTerrainTileCache);
    }
}

internal sealed class ResoniteLiveSceneImportDependencyFactory(
    IResoniteClientSessionFactory clientSessionFactory,
    ITerrainTextureAssetGeneratorFactory terrainTextureAssetGeneratorFactory,
    IResoniteSceneSetupInterpreter sceneSetupInterpreter,
    IResoniteDatasetLicenseWriter datasetLicenseWriter,
    IResoniteMaterialPlanning materialPlanning,
    ILiveSendRunPlanFactory runPlanFactory,
    ILiveSendRunStateFactory runStateFactory,
    IResoniteLiveSendStartRequestFactory startRequestFactory,
    IResonitePreparedCityObjectImporter preparedCityObjectImporter,
    IResoniteSlotCreator slotCreator,
    IResoniteLiveSendQueue queue)
    : IResoniteLiveSceneImportDependencyFactory
{
    public ResoniteLiveSceneImportDependencies Create(
        ResoniteLiveSceneImportTargetOptions options,
        HttpClient terrainTextureAssetHttpClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);
        ResoniteLinkSendDiagnostics diagnostics = options.EnableSendMetrics
            ? ResoniteLinkSendDiagnostics.CreateEnabled(options.ProgressReporter)
            : ResoniteLinkSendDiagnostics.Disabled;

        ResoniteQueuedCityObjectSender queuedCityObjectSender = new(
            terrainTextureAssetGeneratorFactory.Create(terrainTextureAssetHttpClient, options),
            datasetLicenseWriter,
            preparedCityObjectImporter);
        ResoniteQueuedCityObjectWorker queuedCityObjectWorker = new(queuedCityObjectSender);
        ResoniteLiveSendRunStarter runStarter = new(
            sceneSetupInterpreter,
            new ResoniteCommonMaterialSetupPreparer(materialPlanning, options.ProgressReporter),
            runPlanFactory,
            runStateFactory,
            queuedCityObjectWorker,
            slotCreator);

        return new ResoniteLiveSceneImportDependencies(
            clientSessionFactory.Create(options, diagnostics),
            diagnostics,
            startRequestFactory,
            runStarter,
            queue);
    }
}
