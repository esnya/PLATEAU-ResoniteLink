using System;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResonitePreparedCityObjectFactoryFactory
{
    IResonitePreparedCityObjectFactory Create(ITerrainTextureAssetGenerator terrainTextureAssetGenerator);
}

internal sealed class ResonitePreparedCityObjectFactoryFactory(
    IResonitePreparedGeometryFactory preparedGeometryFactory,
    IResonitePreparedTextureReferenceFactoryFactory textureReferenceFactoryFactory) : IResonitePreparedCityObjectFactoryFactory
{
    public IResonitePreparedCityObjectFactory Create(ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetGenerator);

        return new ResonitePreparedCityObjectFactory(
            preparedGeometryFactory,
            textureReferenceFactoryFactory.Create(terrainTextureAssetGenerator));
    }
}
