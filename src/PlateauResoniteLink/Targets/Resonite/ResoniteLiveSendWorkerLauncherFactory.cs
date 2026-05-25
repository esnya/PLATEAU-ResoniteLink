using System;
using System.Net.Http;

namespace PlateauResoniteLink.Targets.Resonite;

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
