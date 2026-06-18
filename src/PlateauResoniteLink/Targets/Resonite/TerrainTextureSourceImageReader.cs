using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Targets.Resonite;

internal interface ITerrainTextureSourceImageReader
{
    Task<TerrainTextureSourceReadResult> TryReadAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        TerrainTextureSource terrainTextureSource,
        CancellationToken cancellationToken);
}

internal interface ITerrainTextureSourceImageReaderFactory
{
    ITerrainTextureSourceImageReader Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options);
}

internal sealed class TerrainTextureSourceImageReaderFactory : ITerrainTextureSourceImageReaderFactory
{
    public ITerrainTextureSourceImageReader Create(
        HttpClient terrainTextureAssetHttpClient,
        ResoniteLiveSceneImportTargetOptions options)
    {
        ArgumentNullException.ThrowIfNull(terrainTextureAssetHttpClient);
        ArgumentNullException.ThrowIfNull(options);

        return new DefaultTerrainTextureSourceImageReader(
            terrainTextureAssetHttpClient,
            options.DisableTerrainTileCache ? null : new PersistentTerrainTileCache(options.TerrainTileCacheRoot));
    }
}

internal sealed class DefaultTerrainTextureSourceImageReader(
    HttpClient httpClient,
    PersistentTerrainTileCache? persistentTileCache) : ITerrainTextureSourceImageReader
{
    private readonly TerrainTextureTileSourceReader tileSourceReader = new(httpClient, persistentTileCache);

    public Task<TerrainTextureSourceReadResult> TryReadAsync(
        TerrainTextureOverlay terrainTextureOverlay,
        TerrainTextureSource terrainTextureSource,
        CancellationToken cancellationToken)
    {
        return terrainTextureSource switch
        {
            TerrainTextureTileSource tileSource => tileSourceReader.TryCreateAsync(
                terrainTextureOverlay,
                tileSource,
                cancellationToken),
            TerrainTextureGeoReferencedRasterSource rasterSource => CreateTextureFromGeoReferencedRasterSourceAsync(
                terrainTextureOverlay.GeographicBounds,
                rasterSource,
                cancellationToken),
            _ => Task.FromResult(TerrainTextureSourceReadResult.CoverageMiss),
        };
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The returned source image owns and disposes the cropped raster image.")]
    private static async Task<TerrainTextureSourceReadResult> CreateTextureFromGeoReferencedRasterSourceAsync(
        GeographicRectangle geographicBounds,
        TerrainTextureGeoReferencedRasterSource rasterSource,
        CancellationToken cancellationToken)
    {
        Image<Rgba32> sourceImage;
        try
        {
            await using Stream sourceStream = await rasterSource.OpenReadAsync(cancellationToken);
            sourceImage = await Image.LoadAsync<Rgba32>(sourceStream, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return TerrainTextureSourceReadResult.SourceFailure(
                $"DEM terrain raster source '{rasterSource.ContentSource.Description}' could not be read as an image: {exception.Message}");
        }

        using (sourceImage)
        {
            Image<Rgba32>? cropped = TerrainTextureGeoReferencedRasterCropper.TryCrop(
                sourceImage,
                rasterSource.Metadata,
                geographicBounds);
            if (cropped is null)
            {
                return TerrainTextureSourceReadResult.CoverageMiss;
            }

            return TerrainTextureSourceReadResult.Rendered(new TerrainTextureSourceImage(cropped, null));
        }
    }
}
