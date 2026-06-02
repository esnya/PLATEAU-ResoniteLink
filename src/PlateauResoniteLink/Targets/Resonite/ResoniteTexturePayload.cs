using System;
using System.Collections.Immutable;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

public enum ResoniteTexturePayloadFormat
{
    RawRgba32,
    EncodedImage,
}

public sealed record ResoniteTexturePayload
{
    public ResoniteTexturePayload(
        int width,
        int height,
        string? colorProfile,
        byte[] binaryPayload,
        string? identity = null)
    {
        Width = width;
        Height = height;
        ColorProfile = colorProfile;
        ArgumentNullException.ThrowIfNull(binaryPayload);
        BinaryPayload = ImmutableArray.CreateRange(binaryPayload);
        Identity = identity;
        Format = ResoniteTexturePayloadFormat.RawRgba32;
        Source = TextureImportSourceFactory.CreateInMemoryRaw(
            width,
            height,
            colorProfile,
            binaryPayload,
            identity ?? Guid.NewGuid().ToString("N"));
    }

    public ResoniteTexturePayload(
        int? width,
        int? height,
        string? colorProfile,
        ITextureImportSource source,
        string? identity = null)
    {
        Width = width;
        Height = height;
        ColorProfile = colorProfile;
        ArgumentNullException.ThrowIfNull(source);
        BinaryPayload = [];
        Identity = identity ?? source.Identity;
        Format = ResoniteTexturePayloadFormat.EncodedImage;
        Source = source;
    }

    public int? Width { get; }

    public int? Height { get; }

    public string? ColorProfile { get; }

    public ImmutableArray<byte> BinaryPayload { get; }

    public string? Identity { get; }

    public ResoniteTexturePayloadFormat Format { get; }

    public ITextureImportSource Source { get; }
}
