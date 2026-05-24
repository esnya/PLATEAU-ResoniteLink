using System.Net.Http;

using PlateauResoniteLink.Targets.Resonite;

namespace PlateauResoniteLink.Tests.Targets;

internal static class TerrainTextureAssetGeneratorTestFactory
{
    public static TerrainTextureAssetGenerator Create(
        HttpClient httpClient,
        PersistentTerrainTileCache? persistentTileCache)
    {
        return new TerrainTextureAssetGenerator(
            new TerrainTextureTileImageLoader(
                httpClient,
                persistentTileCache));
    }
}
