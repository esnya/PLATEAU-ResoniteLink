using System;
using System.Collections.Generic;
using System.Linq;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteQueuedCityObjectSenderFactory
{
    IResoniteQueuedCityObjectSender Create(ITerrainTextureAssetGenerator terrainTextureAssetGenerator);
}

internal sealed class ResoniteQueuedCityObjectSenderFactory(
    IResonitePreparedCityObjectFactoryFactory preparedCityObjectFactoryFactory,
    IResonitePreparedCityObjectImporter preparedCityObjectImporter,
    IEnumerable<IResoniteQueuedCityObjectSender> queuedCityObjectSenders) : IResoniteQueuedCityObjectSenderFactory
{
    public IResoniteQueuedCityObjectSender Create(ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetGenerator);

        IResoniteQueuedCityObjectSender? preRegisteredSender = queuedCityObjectSenders.LastOrDefault();
        if (preRegisteredSender is not null)
        {
            return preRegisteredSender;
        }

        return new ResoniteQueuedCityObjectSender(
            preparedCityObjectFactoryFactory.Create(terrainTextureAssetGenerator),
            preparedCityObjectImporter);
    }
}
