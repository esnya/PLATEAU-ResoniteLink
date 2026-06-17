using System;

using PlateauResoniteLink.Transport.ResoniteLink;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using PlateauResoniteLink.Application.Importing.Contracts;

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
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(colorProfile);

        RawTexturePayload rawPayload = TextureImportSourceFactory.CreateRawPayloadFromImage(
            image,
            colorProfile);
        return new RawRgba32ResoniteTexturePayload(
            image.Width,
            image.Height,
            colorProfile,
            rawPayload.Bytes,
            description);
    }
}
