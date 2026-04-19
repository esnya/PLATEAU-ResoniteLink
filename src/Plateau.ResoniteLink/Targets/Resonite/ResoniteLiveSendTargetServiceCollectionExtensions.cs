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
        services.AddScoped<IResoniteSceneAnchorResolver, ResoniteSceneAnchorResolver>();
        services.AddScoped<IResoniteSceneBootstrapInterpreter>(
            static provider => new ResoniteSceneBootstrapInterpreter(
                provider.GetRequiredService<IResoniteSceneSlotLocator>(),
                provider.GetRequiredService<IResoniteMaterialPlanning>(),
                provider.GetRequiredService<IResoniteSceneAnchorResolver>()));
        services.AddScoped<IResoniteClientSessionFactory, ResoniteClientSessionFactory>();
        services.AddScoped<ITerrainTextureAssetGeneratorFactory, TerrainTextureAssetGeneratorFactory>();
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
    ILiveSendClientSession Create(
        ResoniteLiveSceneImportTargetOptions options,
        ResoniteLinkSendDiagnostics diagnostics);
}

internal sealed class ResoniteClientSessionFactory : IResoniteClientSessionFactory
{
    public ILiveSendClientSession Create(
        ResoniteLiveSceneImportTargetOptions options,
        ResoniteLinkSendDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(options);

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
        ResoniteLiveSceneImportTargetOptions options,
        HttpClient terrainTextureAssetHttpClient);
}

internal sealed class TerrainTextureAssetGeneratorFactory : ITerrainTextureAssetGeneratorFactory
{
    public ITerrainTextureAssetGenerator Create(
        ResoniteLiveSceneImportTargetOptions options,
        HttpClient terrainTextureAssetHttpClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);

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
    IResoniteBufferedCityObjectBakerFactory cityObjectBakerFactory) : IResoniteLiveSceneImportDependencyFactory
{
    public ResoniteLiveSceneImportDependencies Create(
        ResoniteLiveSceneImportTargetOptions options,
        HttpClient terrainTextureAssetHttpClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        ResoniteLinkSendDiagnostics diagnostics = options.EnableSendMetrics
            ? ResoniteLinkSendDiagnostics.CreateEnabled(options.ProgressReporter)
            : ResoniteLinkSendDiagnostics.Disabled;

        return new ResoniteLiveSceneImportDependencies(
            clientSessionFactory.Create(options, diagnostics),
            diagnostics,
            terrainTextureAssetGeneratorFactory.Create(options, terrainTextureAssetHttpClient),
            sceneBootstrapInterpreter,
            geometryAssetAssembler,
            materialPlanning,
            batchEmissionPlanner,
            batchEmitter,
            slotCreator,
            cityObjectBakerFactory);
    }
}
