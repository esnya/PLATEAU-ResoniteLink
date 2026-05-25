using System;
using System.Net.Http;

using PlateauResoniteLink.Targets.Resonite.Execution;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSendRunStarterFactory
{
    IResoniteLiveSendRunStarter Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options);
}

internal sealed class ResoniteLiveSendRunStarterFactory(
    IResoniteSceneSetupInterpreter sceneSetupInterpreter,
    IResoniteLiveSendConnectionInitializer connectionInitializer,
    IResoniteCommonMaterialSetupPreparer commonMaterialSetupPreparer,
    IResoniteCommonMaterialSetupCachePrimer commonMaterialSetupCachePrimer,
    ILiveSendRunPlanFactory runPlanFactory,
    ILiveSendRunStateFactory runStateFactory,
    IResoniteLiveSendWorkerLauncherFactory workerLauncherFactory,
    IResoniteSharedSlotIndexFactory sharedSlotIndexFactory) : IResoniteLiveSendRunStarterFactory
{
    public IResoniteLiveSendRunStarter Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);
        ArgumentNullException.ThrowIfNull(options);

        return new ResoniteLiveSendRunStarter(
            sceneSetupInterpreter,
            connectionInitializer,
            commonMaterialSetupPreparer,
            commonMaterialSetupCachePrimer,
            runPlanFactory,
            runStateFactory,
            workerLauncherFactory.Create(terrainTextureAssetHttpClient, options),
            sharedSlotIndexFactory);
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
