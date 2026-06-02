using System;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Targets.Resonite;

internal static class ResoniteTextureImportFactory
{
    public static ITextureImportSource CreateSourceFromFile(
        string absolutePath,
        string colorProfile = ResoniteTextureColorProfiles.Srgb)
    {
        return TextureImportSourceFactory.CreateFileImage(absolutePath, colorProfile);
    }

    public static ITextureImportSource CreateSourceFromPayload(ResoniteTexturePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return payload.Source;
    }

    public static ResoniteTexturePayload CreatePayloadFromImage(
        Image<Rgba32> image,
        string colorProfile = ResoniteTextureColorProfiles.Srgb,
        string? identity = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(colorProfile);

        RawTexturePayload rawPayload = TextureImportSourceFactory.CreateRawPayloadFromImage(
            image,
            colorProfile);
        return ResoniteTexturePayload.CreateRaw(
            image.Width,
            image.Height,
            colorProfile,
            rawPayload.Bytes,
            identity ?? Guid.NewGuid().ToString("N"));
    }
}
