using System;
using System.Net.Http;

using PlateauResoniteLink.Targets.Resonite.Execution;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface IResoniteQueuedCityObjectPreparerFactory
{
    IResoniteQueuedCityObjectPreparer Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options);
}

internal sealed class ResoniteQueuedCityObjectPreparerFactory(
    IResoniteQueuedGeometryPreparer geometryPreparer,
    ITerrainTextureAssetGeneratorFactory terrainTextureAssetGeneratorFactory,
    IResoniteDatasetLicenseWriter datasetLicenseWriter) : IResoniteQueuedCityObjectPreparerFactory
{
    public IResoniteQueuedCityObjectPreparer Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);
        ArgumentNullException.ThrowIfNull(options);

        ResoniteQueuedTexturePreparer texturePreparer = new(
            terrainTextureAssetGeneratorFactory.Create(terrainTextureAssetHttpClient, options),
            datasetLicenseWriter);
        return new ResoniteQueuedCityObjectPreparer(
            geometryPreparer,
            texturePreparer);
    }
}
