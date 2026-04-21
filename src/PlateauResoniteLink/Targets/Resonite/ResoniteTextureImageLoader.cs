using System;
using System.Threading;
using System.Threading.Tasks;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class ResoniteTextureImageLoader
{
#pragma warning disable CA1822
    public Task<Image<Rgba32>> LoadAsync(
        ResoniteTextureImport textureImport,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(textureImport);

        return textureImport switch
        {
            ResoniteRawTextureImport rawTextureImport => Task.FromResult(
                Image.LoadPixelData<Rgba32>(
                    rawTextureImport.RawRgba32Bytes,
                    rawTextureImport.Width,
                    rawTextureImport.Height)),
            _ => throw new InvalidOperationException(
                $"Unsupported texture import type '{textureImport.GetType().Name}'."),
        };
    }
#pragma warning restore CA1822
}
