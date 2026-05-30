using System;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Targets.Resonite;

internal sealed class ResoniteTextureImageLoader
{
#pragma warning disable CA1822
    public Task<Image<Rgba32>> LoadAsync(
        ITextureImportSource textureSource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(textureSource);

        return LoadCoreAsync(textureSource, cancellationToken);
    }

    private static async Task<Image<Rgba32>> LoadCoreAsync(
        ITextureImportSource textureSource,
        CancellationToken cancellationToken)
    {
        RawTexturePayload rawPayload = await TextureImportSourceMaterializer.MaterializeRawAsync(
            textureSource,
            cancellationToken);
        if (rawPayload.Format != RawTexturePayloadFormat.Rgba32)
        {
            throw new InvalidOperationException(
                $"Unsupported texture payload format '{rawPayload.Format}' for image loading.");
        }

        return Image.LoadPixelData<Rgba32>(
            rawPayload.Bytes,
            rawPayload.Width,
            rawPayload.Height);
    }
#pragma warning restore CA1822
}
