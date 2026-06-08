using System;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class ResoniteLiveSendWorkerPipelineFactory(
    ResonitePreparedCityObjectImporter preparedCityObjectImporter)
{
    public ResoniteQueuedCityObjectWorker Create(ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetGenerator);

        ResoniteQueuedTexturePreparer texturePreparer = new(
            terrainTextureAssetGenerator);
        ResoniteQueuedCityObjectPreparation cityObjectPreparation = new(texturePreparer);
        ResoniteQueuedCityObjectSender queuedCityObjectSender = new(
            cityObjectPreparation,
            preparedCityObjectImporter);
        return new ResoniteQueuedCityObjectWorker(queuedCityObjectSender);
    }
}
