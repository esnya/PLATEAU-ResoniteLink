using System;
using System.Net.Http;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteQueuedCityObjectSenderFactory
{
    IResoniteQueuedCityObjectSender Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options);
}

internal sealed class ResoniteQueuedCityObjectSenderFactory(
    IResoniteQueuedCityObjectPreparerFactory preparerFactory,
    IResoniteQueuedSendFailurePolicy sendFailurePolicy,
    IResonitePreparedCityObjectImporter preparedCityObjectImporter) : IResoniteQueuedCityObjectSenderFactory
{
    public IResoniteQueuedCityObjectSender Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);
        ArgumentNullException.ThrowIfNull(options);

        return new ResoniteQueuedCityObjectSender(
            preparerFactory.Create(terrainTextureAssetHttpClient, options),
            sendFailurePolicy,
            preparedCityObjectImporter);
    }
}
