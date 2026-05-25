using System;
using System.Net.Http;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSceneImportExecutorFactory
{
    IResoniteLiveSceneImportExecutor Create(
        ResoniteLiveSceneImportTargetOptions options,
        HttpClient terrainTextureAssetHttpClient);
}

internal sealed class ResoniteLiveSceneImportExecutorFactory(
    IResoniteLiveSendStartRequestFactory startRequestFactory,
    IResoniteLiveSendRunStarterFactory runStarterFactory,
    IResoniteLiveSendContextFactory contextFactory,
    IResoniteLiveSendResourceReleaser resourceReleaser,
    IResoniteLiveSendQueue queue) : IResoniteLiveSceneImportExecutorFactory
{
    public IResoniteLiveSceneImportExecutor Create(
        ResoniteLiveSceneImportTargetOptions options,
        HttpClient terrainTextureAssetHttpClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);

        return new ResoniteLiveSceneImportExecutor(
            startRequestFactory,
            runStarterFactory.Create(terrainTextureAssetHttpClient, options),
            contextFactory,
            resourceReleaser,
            queue);
    }
}
