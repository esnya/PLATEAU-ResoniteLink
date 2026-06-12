using System;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSendWorkerPipelineFactory
{
    IResoniteQueuedCityObjectWorker Create(ITerrainTextureAssetGenerator terrainTextureAssetGenerator);
}

internal sealed class ResoniteLiveSendWorkerPipelineFactory(
    ResonitePreparedCityObjectImporter preparedCityObjectImporter) : IResoniteLiveSendWorkerPipelineFactory
{
    public IResoniteQueuedCityObjectWorker Create(ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
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
