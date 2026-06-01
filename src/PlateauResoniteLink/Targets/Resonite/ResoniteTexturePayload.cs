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
        Source = TextureImportSourceFactory.CreateRawRgba32InMemory(
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

    internal ResoniteTexturePayload(
        int width,
        int height,
        string? colorProfile,
        IRawTexturePayloadSource source,
        string? identity = null)
    {
        Width = width;
        Height = height;
        ColorProfile = colorProfile;
        ArgumentNullException.ThrowIfNull(source);
        BinaryPayload = [];
        Identity = identity ?? source.Identity;
        Format = ResoniteTexturePayloadFormat.RawRgba32;
        Source = source;
    }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public string? ColorProfile { get; init; }

    public ImmutableArray<byte> BinaryPayload { get; init; }

    public string? Identity { get; init; }

    public ResoniteTexturePayloadFormat Format { get; init; }

    public ITextureImportSource Source { get; init; }
}
