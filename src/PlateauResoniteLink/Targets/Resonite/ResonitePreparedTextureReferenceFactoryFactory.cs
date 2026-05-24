using System;

using PlateauResoniteLink.Targets.Resonite.Execution;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResonitePreparedTextureReferenceFactoryFactory
{
    IResonitePreparedTextureReferenceFactory Create(ITerrainTextureAssetGenerator terrainTextureAssetGenerator);
}

internal sealed class ResonitePreparedTextureReferenceFactoryFactory(
    IResoniteDatasetLicenseWriter datasetLicenseWriter) : IResonitePreparedTextureReferenceFactoryFactory
{
    public IResonitePreparedTextureReferenceFactory Create(ITerrainTextureAssetGenerator terrainTextureAssetGenerator)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetGenerator);

        return new ResonitePreparedTextureReferenceFactory(
            terrainTextureAssetGenerator,
            datasetLicenseWriter);
    }
}
