using System;
using System.Net.Http;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSendRunStarterFactory
{
    IResoniteLiveSendRunStarter Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options);
}

internal sealed class ResoniteLiveSendRunStarterFactory(
    IResoniteLiveSendConnectionInitializer connectionInitializer,
    IResoniteLiveSendSetupInitializer setupInitializer,
    ILiveSendRunPlanFactory runPlanFactory,
    ILiveSendRunStateFactory runStateFactory,
    IResoniteLiveSendWorkerLauncherFactory workerLauncherFactory) : IResoniteLiveSendRunStarterFactory
{
    public IResoniteLiveSendRunStarter Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);
        ArgumentNullException.ThrowIfNull(options);

        return new ResoniteLiveSendRunStarter(
            connectionInitializer,
            setupInitializer,
            runPlanFactory,
            new ResoniteLiveSendRunActivator(
                runStateFactory,
                workerLauncherFactory.Create(terrainTextureAssetHttpClient, options)));
    }
}

internal interface IResoniteLiveSendWorkerLauncherFactory
{
    IResoniteLiveSendWorkerLauncher Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options);
}

internal sealed class ResoniteLiveSendWorkerLauncherFactory(
    IResoniteQueuedCityObjectSenderFactory queuedCityObjectSenderFactory,
    IResoniteQueuedCityObjectLaneProcessorFactory laneProcessorFactory) : IResoniteLiveSendWorkerLauncherFactory
{
    public IResoniteLiveSendWorkerLauncher Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);
        ArgumentNullException.ThrowIfNull(options);

        IResoniteQueuedCityObjectSender queuedCityObjectSender =
            queuedCityObjectSenderFactory.Create(terrainTextureAssetHttpClient, options);
        ResoniteQueuedCityObjectWorker queuedCityObjectWorker = new(
            laneProcessorFactory.Create(queuedCityObjectSender));
        return new ResoniteLiveSendWorkerLauncher(queuedCityObjectWorker);
    }
}
