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
    IResoniteLiveSendRunPlanInitializer runPlanInitializer,
    IResoniteLiveSendRunActivatorFactory runActivatorFactory,
    IResoniteLiveSendWorkerLauncherFactory workerLauncherFactory) : IResoniteLiveSendRunStarterFactory
{
    public IResoniteLiveSendRunStarter Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);
        ArgumentNullException.ThrowIfNull(options);

        IResoniteLiveSendWorkerLauncher workerLauncher =
            workerLauncherFactory.Create(terrainTextureAssetHttpClient, options);
        return new ResoniteLiveSendRunStarter(
            runPlanInitializer,
            connectionInitializer,
            setupInitializer,
            runActivatorFactory.Create(workerLauncher));
    }
}
