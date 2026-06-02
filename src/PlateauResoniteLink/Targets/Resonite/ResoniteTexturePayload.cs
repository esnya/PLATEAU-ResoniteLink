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
        ArgumentNullException.ThrowIfNull(binaryPayload);
        RawTexturePayload.EnsureValidShape(width, height, binaryPayload.Length, RawTexturePayloadFormat.Rgba32);
        ImmutableArray<byte> immutablePayload = ImmutableArray.CreateRange(binaryPayload);
        string effectiveIdentity = identity ?? Guid.NewGuid().ToString("N");
        Width = width;
        Height = height;
        ColorProfile = colorProfile;
        BinaryPayload = immutablePayload;
        Identity = effectiveIdentity;
        Format = ResoniteTexturePayloadFormat.RawRgba32;
        Source = TextureImportSourceFactory.CreateRawRgba32InMemory(
            width,
            height,
            colorProfile,
            immutablePayload,
            effectiveIdentity);
    }

    public ResoniteTexturePayload(
        int? width,
        int? height,
        string? colorProfile,
        ITextureImportSource source,
        string? identity = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        string effectiveIdentity = identity ?? source.Identity;
        if (!string.Equals(effectiveIdentity, source.Identity, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Texture payload identity must match the texture import source identity.",
                nameof(identity));
        }

        Width = width;
        Height = height;
        ColorProfile = colorProfile;
        BinaryPayload = [];
        Identity = effectiveIdentity;
        Format = ResoniteTexturePayloadFormat.EncodedImage;
        Source = source;
    }

    internal ResoniteTexturePayload(
        int width,
        int height,
        string? colorProfile,
        IRawTexturePayloadSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        RawTexturePayload.EnsureValidDimensions(width, height);
        Width = width;
        Height = height;
        ColorProfile = colorProfile;
        BinaryPayload = [];
        Identity = source.Identity;
        Format = ResoniteTexturePayloadFormat.RawRgba32;
        Source = source;
    }

    public int? Width { get; }

    public int? Height { get; }

    public string? ColorProfile { get; }

    public ImmutableArray<byte> BinaryPayload { get; }

    public string Identity { get; }

    public ResoniteTexturePayloadFormat Format { get; }

    public ITextureImportSource Source { get; }
}
