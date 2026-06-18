using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using PlateauResoniteLink.Core;
using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite.Execution;
using PlateauResoniteLink.Targets.Resonite.Diagnostics;
using PlateauResoniteLink.Transport.ResoniteLink;

namespace PlateauResoniteLink.Targets.Resonite;

public static class ResoniteLiveSendTargetServiceCollectionExtensions
{
    public static IServiceCollection AddResoniteLiveSendTargetServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<Func<IResoniteLinkClient>>(
            _ => static () => new ResoniteLinkClient());
        services.TryAddScoped<BundledDefaultMaterialAssetStore>();
        services.TryAddScoped<ResoniteTextureImageLoader>();
        services.TryAddScoped<INonDemSourceFileBakeEmitterFactory, NonDemSourceFileBakeEmitterFactory>();
        services.TryAddScoped<ResoniteBufferedCityObjectBakerFactory>();
        services.TryAddScoped<IResoniteMaterialPlanning, ResoniteMaterialPlanning>();
        services.TryAddScoped<ResoniteSceneMaterialPlanComposer>();
        services.TryAddScoped<ResoniteCommonMaterialSetupPreparer>();
        services.TryAddScoped<ResonitePreparedRunSetupComposer>();
        services.TryAddScoped<IResoniteLiveSendRunSetupPreparer, ResoniteLiveSendRunSetupPreparer>();
        services.TryAddScoped<LiveSendRunStateFactory>();
        services.TryAddScoped<ResoniteLiveSendRunStarterFactory>();
        services.TryAddScoped<IResoniteLiveSendRunExecutorFactory, ResoniteLiveSendRunExecutorFactory>();
        services.TryAddScoped<IResoniteLiveSendWorkerPipelineFactory, ResoniteLiveSendWorkerPipelineFactory>();
        services.TryAddScoped<ITerrainTextureAssetGeneratorFactory, MissingTerrainTextureAssetGeneratorFactory>();
        services.TryAddScoped<ResoniteLiveSendWorkerLauncherFactory>();
        services.TryAddScoped<ResonitePreparedCityObjectImporter>();
        services.TryAddScoped<IResoniteCanonicalSceneDumpSinkFactory, ResoniteCanonicalSceneDumpSinkFactory>();
        services.TryAddScoped<IResoniteSlotCreator, ResoniteSlotCreator>();
        services.TryAddScoped<IResoniteSceneAnchorResolver, ResoniteSceneAnchorResolver>();
        services.TryAddScoped<IResoniteSceneSlotLocator, ResoniteSceneSlotLocator>();
        services.TryAddScoped<IResoniteSceneSetupObserver, ResoniteSceneSetupObserver>();
        services.TryAddScoped<IResoniteClientSessionFactory, ResoniteLinkClientSessionFactory>();
        services.TryAddScoped<IResoniteSceneSetupInterpreter>(
            static serviceProvider => new ResoniteSceneSetupInterpreter(
                serviceProvider.GetRequiredService<IResoniteSceneSetupObserver>(),
                serviceProvider.GetRequiredService<IResoniteSceneAnchorResolver>()));
        services.TryAddScoped<ResoniteLiveSceneImportDependencyFactory>();
        services.TryAddScoped<ResoniteLiveSceneImportFactory>();
        services.TryAddScoped<IResoniteLiveSceneImportFactory>(
            static serviceProvider => serviceProvider.GetRequiredService<ResoniteLiveSceneImportFactory>());
        services.TryAddScoped<IResoniteRecordingLiveSceneImportFactory>(
            static serviceProvider =>
            {
                IResoniteLiveSceneImportFactory targetFactory =
                    serviceProvider.GetRequiredService<IResoniteLiveSceneImportFactory>();
                return targetFactory as IResoniteRecordingLiveSceneImportFactory
                    ?? throw new InvalidOperationException(
                        "The registered Resonite live scene import factory does not support recording client sessions.");
            });

        return services;
    }

    private sealed class MissingTerrainTextureAssetGeneratorFactory : ITerrainTextureAssetGeneratorFactory
    {
        public ITerrainTextureAssetGenerator Create(
            HttpClient terrainTextureAssetHttpClient,
            TerrainTextureAssetGeneratorOptions options)
        {
            ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);
            ArgumentNullException.ThrowIfNull(options);

            return MissingTerrainTextureAssetGenerator.Instance;
        }
    }

    private sealed class MissingTerrainTextureAssetGenerator : ITerrainTextureAssetGenerator
    {
        public static MissingTerrainTextureAssetGenerator Instance { get; } = new();

        public Task<GeneratedTerrainTexture> EnsureTextureAsync(
            TerrainTextureOverlay terrainTextureOverlay,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(terrainTextureOverlay);
            cancellationToken.ThrowIfCancellationRequested();

            throw new InvalidOperationException(
                "Terrain texture generation is not configured. Register a Plateau terrain texture source provider in the application composition root.");
        }
    }
}
