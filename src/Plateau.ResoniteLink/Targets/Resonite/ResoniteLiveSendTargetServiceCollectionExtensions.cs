using Microsoft.Extensions.DependencyInjection;

using Plateau.ResoniteLink.Targets.Resonite.Execution;

namespace Plateau.ResoniteLink.Targets.Resonite;

public static class ResoniteLiveSendTargetServiceCollectionExtensions
{
    public static IServiceCollection AddResoniteLiveSendTargetServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IResoniteBatchEmissionPlanner, ResoniteBatchEmissionPlanner>();
        services.AddScoped<IResoniteBufferedCityObjectBakerFactory, ResoniteBufferedCityObjectBakerFactory>();
        services.AddScoped<IResoniteGeometryAssetAssembler, ResoniteGeometryAssetAssembler>();
        services.AddScoped<IResoniteMaterialPlanning, ResoniteMaterialPlanning>();
        services.AddScoped<IResoniteSceneBatchEmitter, PlannedBatchEmissionInterpreter>();
        services.AddScoped<IResoniteSlotCreator, ResoniteSlotCreator>();
        services.AddScoped<IResoniteSceneAnchorResolver, ResoniteSceneAnchorResolver>();
        services.AddScoped<IResoniteSceneSlotLocator, ResoniteSceneSlotLocator>();
        services.AddScoped<IResoniteClientSessionFactory, ResoniteClientSessionFactory>();
        services.AddScoped<ITerrainTextureAssetGeneratorFactory, TerrainTextureAssetGeneratorFactory>();
        services.AddScoped<IResoniteSceneBootstrapInterpreter>(
            static serviceProvider => new ResoniteSceneBootstrapInterpreter(
                serviceProvider.GetRequiredService<IResoniteSceneSlotLocator>(),
                serviceProvider.GetRequiredService<IResoniteMaterialPlanning>(),
                serviceProvider.GetRequiredService<IResoniteSceneAnchorResolver>()));
        services.AddScoped<IResoniteLiveSceneImportDependencyFactory, ResoniteLiveSceneImportDependencyFactory>();
        services.AddScoped<IResoniteLiveSceneImportFactory, ResoniteLiveSceneImportFactory>();

        return services;
    }
}

public interface IResoniteLiveSceneImportFactory
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
    ILiveSendClientSession Create(ResoniteLiveSceneImportTargetOptions options, ResoniteLinkSendDiagnostics diagnostics);
}

internal sealed class ResoniteClientSessionFactory : IResoniteClientSessionFactory
{
    public ILiveSendClientSession Create(ResoniteLiveSceneImportTargetOptions options, ResoniteLinkSendDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(diagnostics);
        return ResoniteLinkTransportSessionFactory.Create(
            options.Endpoint,
            options.ConnectionCount,
            diagnostics,
            options.ProgressReporter);
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
    IResoniteSceneBootstrapInterpreter sceneBootstrapInterpreter,
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
            geometryAssetAssembler,
            materialPlanning,
            batchEmissionPlanner,
            batchEmitter,
            slotCreator,
            cityObjectBakerFactory);
    }
}
