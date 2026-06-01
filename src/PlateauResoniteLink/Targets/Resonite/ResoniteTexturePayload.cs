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
        int? width,
        int? height,
        string? colorProfile,
        byte[] binaryPayload,
        string identity,
        ResoniteTexturePayloadFormat format = ResoniteTexturePayloadFormat.RawRgba32)
    {
        Width = width;
        Height = height;
        ColorProfile = colorProfile;
        ArgumentNullException.ThrowIfNull(binaryPayload);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        BinaryPayload = ImmutableArray.CreateRange(binaryPayload);
        Identity = identity;
        Format = format;
        Source = TextureImportSourceFactory.CreateInMemory(
            width,
            height,
            colorProfile,
            binaryPayload,
            identity,
            (TexturePayloadFormat)format);
    }

    public ResoniteTexturePayload(
        int? width,
        int? height,
        string? colorProfile,
        ITextureImportSource source,
        string? identity = null,
        ResoniteTexturePayloadFormat format = ResoniteTexturePayloadFormat.EncodedImage)
    {
        Width = width;
        Height = height;
        ColorProfile = colorProfile;
        ArgumentNullException.ThrowIfNull(source);
        BinaryPayload = [];
        string resolvedIdentity = identity ?? source.Identity;
        ArgumentException.ThrowIfNullOrWhiteSpace(resolvedIdentity, nameof(identity));
        Identity = resolvedIdentity;
        Format = format;
        Source = source;
    }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public string? ColorProfile { get; init; }

    public ImmutableArray<byte> BinaryPayload { get; init; }

    public string Identity { get; init; }

    public ResoniteTexturePayloadFormat Format { get; init; }

    public ITextureImportSource Source { get; init; }
}
