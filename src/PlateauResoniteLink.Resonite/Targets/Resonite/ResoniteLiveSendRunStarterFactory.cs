using System;

using PlateauResoniteLink.Core;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

internal sealed class ResoniteLiveSendRunStarterFactory(
    IResoniteLiveSendRunSetupPreparer runSetupPreparer,
    LiveSendRunStateFactory runStateFactory,
    ResoniteLiveSendWorkerLauncherFactory workerLauncherFactory)
{
    public ResoniteLiveSendRunStarter Create(ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetGenerator);

        return Create(workerLauncherFactory.Create(terrainTextureAssetGenerator));
    }

    private ResoniteLiveSendRunStarter Create(ResoniteLiveSendWorkerLauncher workerLauncher)
    {
        ArgumentNullException.ThrowIfNull(workerLauncher);

        return new ResoniteLiveSendRunStarter(
            runSetupPreparer,
            runStateFactory,
            workerLauncher);
    }
}

internal sealed class ResoniteLiveSendWorkerLauncherFactory(
    IResoniteLiveSendWorkerPipelineFactory workerPipelineFactory)
{
    public ResoniteLiveSendWorkerLauncher Create(
        ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetGenerator);

        return new ResoniteLiveSendWorkerLauncher(workerPipelineFactory.Create(terrainTextureAssetGenerator));
    }
}
