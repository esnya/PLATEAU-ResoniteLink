using System;

using PlateauResoniteLink.Targets.Resonite.Execution;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteLiveSendWorkerPipelineFactory
{
    IResoniteQueuedCityObjectWorker Create(ITerrainTextureAssetGenerator terrainTextureAssetGenerator);
}

internal sealed class ResoniteLiveSendWorkerPipelineFactory(
    IResoniteDatasetLicenseWriter datasetLicenseWriter,
    IResonitePreparedCityObjectImporter preparedCityObjectImporter) : IResoniteLiveSendWorkerPipelineFactory
{
    public IResoniteQueuedCityObjectWorker Create(ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetGenerator);

        ResoniteQueuedTexturePreparer texturePreparer = new(
            terrainTextureAssetGenerator,
            datasetLicenseWriter);
        ResoniteQueuedCityObjectSender queuedCityObjectSender = new(
            texturePreparer,
            preparedCityObjectImporter);
        return new ResoniteQueuedCityObjectWorker(queuedCityObjectSender);
    }
}
