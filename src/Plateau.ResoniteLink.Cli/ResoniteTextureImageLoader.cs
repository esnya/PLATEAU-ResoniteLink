using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Plateau.ResoniteLink.Cli;

internal sealed class ResoniteTextureImageLoader(IPlateauDatasetContentSource datasetContentSource)
{
    public async Task<Image<Rgba32>> LoadAsync(
        string texturePath,
        ResoniteTextureSourceKind textureSourceKind,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(texturePath);

        return textureSourceKind switch
        {
            ResoniteTextureSourceKind.Dataset => await LoadDatasetAsync(texturePath, cancellationToken),
            ResoniteTextureSourceKind.Bundled => await Image.LoadAsync<Rgba32>(
                BundledDefaultMaterialAssetStore.GetAbsolutePath(texturePath),
                cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported texture source kind '{textureSourceKind}'."),
        };
    }

    private async Task<Image<Rgba32>> LoadDatasetAsync(
        string texturePath,
        CancellationToken cancellationToken)
    {
        await using Stream textureStream = await datasetContentSource.OpenReadAsync(texturePath, cancellationToken);
        return await Image.LoadAsync<Rgba32>(textureStream, cancellationToken);
    }
}
