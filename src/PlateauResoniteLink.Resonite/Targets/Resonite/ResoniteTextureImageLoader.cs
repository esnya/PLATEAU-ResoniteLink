using System;
using System.Threading;
using System.Threading.Tasks;


using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using PlateauResoniteLink.Core.Application.Importing.Contracts;

namespace PlateauResoniteLink.Resonite.Targets.Resonite;

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
        Rgba32RawTexturePayload rawPayload = await TextureImportSourceMaterializer.MaterializeRgba32Async(
            textureSource,
            cancellationToken);

        return Image.LoadPixelData<Rgba32>(
            rawPayload.Bytes,
            rawPayload.Width,
            rawPayload.Height);
    }
#pragma warning restore CA1822
}
