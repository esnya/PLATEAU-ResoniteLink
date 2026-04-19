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
        services.AddScoped<IResoniteSceneSlotLocator, ResoniteSceneSlotLocator>();
        services.AddScoped<IResoniteSceneBootstrapInterpreter>(
            static serviceProvider => new ResoniteSceneBootstrapInterpreter(
                serviceProvider.GetRequiredService<IResoniteSceneSlotLocator>(),
                serviceProvider.GetRequiredService<IResoniteMaterialPlanning>()));
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

internal sealed class ResoniteLiveSceneImportDependencyFactory(IServiceProvider serviceProvider)
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
            ResoniteLinkTransportSessionFactory.Create(
                options.Endpoint,
                options.ConnectionCount,
                diagnostics,
                options.ProgressReporter),
            diagnostics,
            new TerrainTextureAssetGenerator(
                terrainTextureAssetHttpClient,
                options.TerrainTileCacheRoot,
                options.DisableTerrainTileCache),
            serviceProvider.GetRequiredService<IResoniteSceneBootstrapInterpreter>(),
            serviceProvider.GetRequiredService<IResoniteGeometryAssetAssembler>(),
            serviceProvider.GetRequiredService<IResoniteMaterialPlanning>(),
            serviceProvider.GetRequiredService<IResoniteBatchEmissionPlanner>(),
            serviceProvider.GetRequiredService<IResoniteSceneBatchEmitter>(),
            serviceProvider.GetRequiredService<IResoniteSlotCreator>(),
            serviceProvider.GetRequiredService<IResoniteBufferedCityObjectBakerFactory>());
    }
}
