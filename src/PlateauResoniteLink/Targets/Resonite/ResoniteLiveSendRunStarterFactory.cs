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
    IResoniteSlotCreator slotCreator) : IResoniteLiveSendRunStarterFactory
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
            slotCreator);
    }
}

internal interface IResoniteLiveSendWorkerLauncherFactory
{
    IResoniteLiveSendWorkerLauncher Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options);
}

internal sealed class ResoniteLiveSendWorkerLauncherFactory(
    ITerrainTextureAssetGeneratorFactory terrainTextureAssetGeneratorFactory,
    IResoniteDatasetLicenseWriter datasetLicenseWriter,
    IResonitePreparedCityObjectImporter preparedCityObjectImporter) : IResoniteLiveSendWorkerLauncherFactory
{
    public IResoniteLiveSendWorkerLauncher Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);
        ArgumentNullException.ThrowIfNull(options);

        ResoniteQueuedTexturePreparer texturePreparer = new(
            terrainTextureAssetGeneratorFactory.Create(terrainTextureAssetHttpClient, options),
            datasetLicenseWriter);
        ResoniteQueuedCityObjectSender queuedCityObjectSender = new(
            new ResoniteQueuedCityObjectPreparer(
                new ResoniteQueuedGeometryPreparer(),
                texturePreparer),
            new ResoniteQueuedSendFailurePolicy(),
            preparedCityObjectImporter);
        ResoniteQueuedCityObjectWorker queuedCityObjectWorker = new(queuedCityObjectSender);
        return new ResoniteLiveSendWorkerLauncher(queuedCityObjectWorker);
    }
}
