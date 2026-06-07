using System;
using System.Net.Http;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class ResoniteLiveSendRunStarterFactory(
    ILiveSendRunPlanFactory runPlanFactory,
    IResoniteLiveSendConnectionInitializer connectionInitializer,
    IResoniteLiveSendRunSetupPreparer runSetupPreparer,
    LiveSendRunStateFactory runStateFactory,
    ResoniteLiveSendWorkerLauncherFactory workerLauncherFactory)
{
    public ResoniteLiveSendRunStarter Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);
        ArgumentNullException.ThrowIfNull(options);

        return Create(workerLauncherFactory.Create(terrainTextureAssetHttpClient, options));
    }

    public ResoniteLiveSendRunStarter Create(ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
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

internal sealed class ResoniteLiveSendWorkerLauncherFactory(
    ITerrainTextureAssetGeneratorFactory terrainTextureAssetGeneratorFactory,
    IResoniteLiveSendWorkerPipelineFactory workerPipelineFactory)
{
    public ResoniteLiveSendWorkerLauncher Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);
        ArgumentNullException.ThrowIfNull(options);

        return Create(terrainTextureAssetGeneratorFactory.Create(terrainTextureAssetHttpClient, options));
    }

    public ResoniteLiveSendWorkerLauncher Create(
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetGenerator);

        return new ResoniteLiveSendWorkerLauncher(workerPipelineFactory.Create(terrainTextureAssetGenerator));
    }
}
