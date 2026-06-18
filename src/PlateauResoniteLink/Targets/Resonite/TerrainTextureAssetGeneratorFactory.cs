using System;
using System.Net.Http;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface ITerrainTextureAssetGeneratorFactory
{
    ITerrainTextureAssetGenerator Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options);
}

internal sealed class TerrainTextureAssetGeneratorFactory(
    ITerrainTextureSourceImageReaderFactory sourceImageReaderFactory) : ITerrainTextureAssetGeneratorFactory
{
    private readonly ITerrainTextureSourceImageReaderFactory sourceImageReaderFactory =
        sourceImageReaderFactory ?? throw new ArgumentNullException(nameof(sourceImageReaderFactory));

    public ITerrainTextureAssetGenerator Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);
        ArgumentNullException.ThrowIfNull(options);
        return new TerrainTextureAssetGenerator(sourceImageReaderFactory.Create(
            terrainTextureAssetHttpClient,
            options));
    }
}
