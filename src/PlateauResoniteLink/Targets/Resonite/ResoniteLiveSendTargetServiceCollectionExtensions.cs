using System;
using System.Net.Http;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using PlateauResoniteLink.Targets.Resonite.Execution;

using ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

public static class ResoniteLiveSendTargetServiceCollectionExtensions
{
    public static IServiceCollection AddResoniteLiveSendTargetServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<BundledDefaultMaterialAssetStore>();
        services.TryAddScoped<IResoniteBatchEmissionPlanner, ResoniteBatchEmissionPlanner>();
        services.TryAddScoped<IResoniteBufferedCityObjectBakerFactory, ResoniteBufferedCityObjectBakerFactory>();
        services.TryAddScoped<IResoniteGeometryAssetAssembler, ResoniteGeometryAssetAssembler>();
        services.TryAddScoped<IResoniteMaterialPlanning, ResoniteMaterialPlanning>();
        services.TryAddScoped<IResoniteSceneBatchEmitter, PlannedBatchEmissionInterpreter>();
        services.TryAddScoped<IResoniteSlotCreator, ResoniteSlotCreator>();
        services.TryAddScoped<IResoniteSceneAnchorResolver, ResoniteSceneAnchorResolver>();
        services.TryAddScoped<IResoniteSceneSlotLocator, ResoniteSceneSlotLocator>();
        services.TryAddScoped<Func<IResoniteLinkClient>>(
            static _ => () => new ResoniteLinkClient(new LinkInterfaceResoniteLinkTransport(new LinkInterface())));
        services.TryAddScoped<IResoniteClientSessionFactory, ResoniteLinkClientSessionFactory>();
        services.TryAddScoped<ITerrainTextureAssetGeneratorFactory, TerrainTextureAssetGeneratorFactory>();
        services.TryAddScoped<IResoniteSceneBootstrapInterpreter>(
            static serviceProvider => new ResoniteSceneBootstrapInterpreter(
                serviceProvider.GetRequiredService<IResoniteSceneSlotLocator>(),
                serviceProvider.GetRequiredService<IResoniteMaterialPlanning>(),
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
    IResoniteSceneBootstrapInterpreter sceneBootstrapInterpreter,
    IResoniteDatasetLicenseWriter datasetLicenseWriter,
    IResoniteGeometryAssetAssembler geometryAssetAssembler,
    IResoniteMaterialPlanning materialPlanning,
    IResoniteBatchEmissionPlanner batchEmissionPlanner,
    IResoniteSceneBatchEmitter batchEmitter,
    IResoniteSlotCreator slotCreator,
    IResoniteBufferedCityObjectBakerFactory cityObjectBakerFactory)
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

        return new ResoniteLiveSceneImportDependencies(
            clientSessionFactory.Create(options, diagnostics),
            diagnostics,
            terrainTextureAssetGeneratorFactory.Create(terrainTextureAssetHttpClient, options),
            sceneBootstrapInterpreter,
            datasetLicenseWriter,
            geometryAssetAssembler,
            materialPlanning,
            batchEmissionPlanner,
            batchEmitter,
            slotCreator,
            cityObjectBakerFactory);
    }
}
