using System;
using System.Net.Http;

using PlateauResoniteLink.Targets.Resonite.Execution;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSendRunStarterFactory
{
    IResoniteLiveSendRunStarter Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options);

    IResoniteLiveSendRunStarter Create(
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator,
        ResoniteLiveSceneImportTargetOptions options);
}

internal sealed class ResoniteLiveSendRunStarterFactory(
    ILiveSendRunPlanFactory runPlanFactory,
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

        return Create(
            workerLauncherFactory.Create(terrainTextureAssetHttpClient, options),
            options);
    }

    public IResoniteLiveSendRunStarter Create(
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator,
        ResoniteLiveSceneImportTargetOptions options)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetGenerator);
        ArgumentNullException.ThrowIfNull(options);

        return Create(
            workerLauncherFactory.Create(terrainTextureAssetGenerator),
            options);
    }

    private ResoniteLiveSendRunStarter Create(
        IResoniteLiveSendWorkerLauncher workerLauncher,
        ResoniteLiveSceneImportTargetOptions options)
    {
        ArgumentNullException.ThrowIfNull(workerLauncher);
        ArgumentNullException.ThrowIfNull(options);

        return new ResoniteLiveSendRunStarter(
            runPlanFactory,
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
    IResoniteDatasetLicenseWriter datasetLicenseWriter,
    IResonitePreparedCityObjectImporter preparedCityObjectImporter) : IResoniteLiveSendWorkerLauncherFactory
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

        ResoniteQueuedTexturePreparer texturePreparer = new(
            terrainTextureAssetGenerator,
            datasetLicenseWriter);
        ResoniteQueuedCityObjectSender queuedCityObjectSender = new(
            texturePreparer,
            preparedCityObjectImporter);
        ResoniteQueuedCityObjectWorker queuedCityObjectWorker = new(queuedCityObjectSender);
        return new ResoniteLiveSendWorkerLauncher(queuedCityObjectWorker);
    }
}
