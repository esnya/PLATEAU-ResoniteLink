using System;
using System.Net.Http;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface ITerrainTextureAssetGeneratorFactory
{
    ITerrainTextureAssetGenerator Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options);
}

internal sealed class TerrainTextureAssetGeneratorFactory : ITerrainTextureAssetGeneratorFactory
{
    public ITerrainTextureAssetGenerator Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);
        ArgumentNullException.ThrowIfNull(options);
        PersistentTerrainTileCache? persistentTileCache = options.DisableTerrainTileCache
            ? null
            : new PersistentTerrainTileCache(options.TerrainTileCacheRoot);
        return new TerrainTextureAssetGenerator(
            new TerrainTextureTileImageLoader(
                terrainTextureAssetHttpClient,
                persistentTileCache));
    }
}
