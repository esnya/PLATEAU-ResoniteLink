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
        string identity,
        string colorProfile = ResoniteTextureColorProfiles.Srgb)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(colorProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);

        RawTexturePayload rawPayload = TextureImportSourceFactory.CreateRawPayloadFromImage(
            image,
            colorProfile);
        return new ResoniteTexturePayload(
            image.Width,
            image.Height,
            colorProfile,
            rawPayload.Bytes,
            identity,
            ResoniteTexturePayloadFormat.RawRgba32);
    }
}
