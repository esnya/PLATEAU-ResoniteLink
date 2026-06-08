using System;
using System.Net.Http;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class ResoniteLiveSendRunStarterFactory(
    IResoniteLiveSendRunSetupPreparer runSetupPreparer,
    LiveSendRunStateFactory runStateFactory,
    ResoniteLiveSendWorkerPipelineFactory workerPipelineFactory)
{
    public ResoniteLiveSendRunStarter Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);
        ArgumentNullException.ThrowIfNull(options);

        ITerrainTextureAssetGenerator terrainTextureAssetGenerator = new TerrainTextureAssetGenerator(
            terrainTextureAssetHttpClient,
            options.TerrainTileCacheRoot,
            options.DisableTerrainTileCache);
        return Create(terrainTextureAssetGenerator);
    }

    public ResoniteLiveSendRunStarter Create(ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetGenerator);

        return new ResoniteLiveSendRunStarter(
            runSetupPreparer,
            runStateFactory,
            new ResoniteLiveSendWorkerLauncher(workerPipelineFactory.Create(terrainTextureAssetGenerator)));
    }
}
