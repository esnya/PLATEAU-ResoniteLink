using System;

using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed record TerrainTextureSourceUsage(
    string Key,
    string Description,
    bool RequiresGsiFallbackLicense,
    string TextureImportName)
{
    public static TerrainTextureSourceUsage FromSource(TerrainTextureSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source switch
        {
            TerrainTextureTileSource tileSource => new TerrainTextureSourceUsage(
                Key: tileSource.Description,
                Description: tileSource.Description,
                RequiresGsiFallbackLicense: DemTerrainTextureDefaults.IsGsiFallbackSource(tileSource),
                TextureImportName: nameof(TerrainTextureTileSource)),
            TerrainTextureGeoReferencedRasterSource rasterSource => new TerrainTextureSourceUsage(
                Key: rasterSource.Description,
                Description: rasterSource.Description,
                RequiresGsiFallbackLicense: false,
                TextureImportName: nameof(TerrainTextureGeoReferencedRasterSource)),
            _ => new TerrainTextureSourceUsage(
                Key: source.Description,
                Description: source.Description,
                RequiresGsiFallbackLicense: false,
                TextureImportName: source.GetType().Name),
        };
    }
}
