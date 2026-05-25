using System;
using System.Net.Http;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSendRunStarterFactory
{
    IResoniteLiveSendRunStarter Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options);

    IResoniteLiveSendRunStarter Create(ITerrainTextureAssetGenerator terrainTextureAssetGenerator);
}

internal sealed class ResoniteLiveSendRunStarterFactory(
    ILiveSendRunPlanFactory runPlanFactory,
    IResoniteLiveSendConnectionInitializer connectionInitializer,
    IResoniteLiveSendRunSetupPreparer runSetupPreparer,
    ILiveSendRunStateFactory runStateFactory,
    IResoniteLiveSendWorkerLauncherFactory workerLauncherFactory) : IResoniteLiveSendRunStarterFactory
{
    public IResoniteLiveSendRunStarter Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);
        ArgumentNullException.ThrowIfNull(options);

        return Create(workerLauncherFactory.Create(terrainTextureAssetHttpClient, options));
    }

    public IResoniteLiveSendRunStarter Create(ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetGenerator);

        return Create(workerLauncherFactory.Create(terrainTextureAssetGenerator));
    }

    private ResoniteLiveSendRunStarter Create(IResoniteLiveSendWorkerLauncher workerLauncher)
    {
        ArgumentNullException.ThrowIfNull(workerLauncher);

        return new ResoniteLiveSendRunStarter(
            runPlanFactory,
            connectionInitializer,
            runSetupPreparer,
            runStateFactory,
            workerLauncher);
    }
}

internal interface IResoniteLiveSendWorkerLauncherFactory
{
    IResoniteLiveSendWorkerLauncher Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options);

    IResoniteLiveSendWorkerLauncher Create(
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator);
}

internal sealed class ResoniteLiveSendWorkerLauncherFactory(
    ITerrainTextureAssetGeneratorFactory terrainTextureAssetGeneratorFactory,
    IResoniteLiveSendWorkerPipelineFactory workerPipelineFactory) : IResoniteLiveSendWorkerLauncherFactory
{
    public IResoniteLiveSendWorkerLauncher Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);
        ArgumentNullException.ThrowIfNull(options);

        return Create(terrainTextureAssetGeneratorFactory.Create(terrainTextureAssetHttpClient, options));
    }

    public IResoniteLiveSendWorkerLauncher Create(
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetGenerator);

        return new ResoniteLiveSendWorkerLauncher(workerPipelineFactory.Create(terrainTextureAssetGenerator));
    }
}
