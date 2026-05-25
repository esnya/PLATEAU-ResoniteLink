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
    IResoniteCommonMaterialSetupPreparer commonMaterialSetupPreparer,
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
            commonMaterialSetupPreparer,
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
    IResoniteQueuedCityObjectSenderFactory queuedCityObjectSenderFactory) : IResoniteLiveSendWorkerLauncherFactory
{
    public IResoniteLiveSendWorkerLauncher Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);
        ArgumentNullException.ThrowIfNull(options);

        ResoniteQueuedCityObjectWorker queuedCityObjectWorker = new(
            queuedCityObjectSenderFactory.Create(terrainTextureAssetHttpClient, options));
        return new ResoniteLiveSendWorkerLauncher(queuedCityObjectWorker);
    }
}
