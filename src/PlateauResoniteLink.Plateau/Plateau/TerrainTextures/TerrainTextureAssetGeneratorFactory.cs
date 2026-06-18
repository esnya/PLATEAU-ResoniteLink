using System;
using System.Net.Http;

using PlateauResoniteLink.Core;

namespace PlateauResoniteLink.Plateau.TerrainTextures;

internal sealed class TerrainTextureAssetGeneratorFactory(
    ITerrainTextureSourceImageReaderFactory sourceImageReaderFactory) : ITerrainTextureAssetGeneratorFactory
{
    private readonly ITerrainTextureSourceImageReaderFactory sourceImageReaderFactory =
        sourceImageReaderFactory ?? throw new ArgumentNullException(nameof(sourceImageReaderFactory));

    public ITerrainTextureAssetGenerator Create(
        HttpClient terrainTextureAssetHttpClient,
        TerrainTextureAssetGeneratorOptions options)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);
        ArgumentNullException.ThrowIfNull(options);
        return new TerrainTextureAssetGenerator(sourceImageReaderFactory.Create(
            terrainTextureAssetHttpClient,
            options));
    }
}
