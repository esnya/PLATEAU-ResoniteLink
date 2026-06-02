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
    private ResoniteTexturePayload(
        int width,
        int height,
        string? colorProfile,
        ImmutableArray<byte> binaryPayload,
        string? identity = null)
    {
        if (binaryPayload.IsDefault)
        {
            throw new ArgumentException("Raw texture bytes must be initialized.", nameof(binaryPayload));
        }

        RawTexturePayload.EnsureValidShape(width, height, binaryPayload.Length, RawTexturePayloadFormat.Rgba32);
        TextureImportSourceIdentity effectiveIdentity = new(identity ?? Guid.NewGuid().ToString("N"));
        Width = width;
        Height = height;
        ColorProfile = colorProfile;
        BinaryPayload = binaryPayload;
        Format = ResoniteTexturePayloadFormat.RawRgba32;
        Source = TextureImportSourceFactory.CreateRawRgba32InMemory(
            width,
            height,
            colorProfile,
            binaryPayload,
            effectiveIdentity.Value);
    }

    internal static ResoniteTexturePayload CreateRaw(
        int width,
        int height,
        string? colorProfile,
        ImmutableArray<byte> binaryPayload,
        string? identity = null)
    {
        return new ResoniteTexturePayload(width, height, colorProfile, binaryPayload, identity);
    }

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
        TextureImportSourceIdentity effectiveIdentity = new(identity ?? Guid.NewGuid().ToString("N"));
        Width = width;
        Height = height;
        ColorProfile = colorProfile;
        BinaryPayload = immutablePayload;
        Format = ResoniteTexturePayloadFormat.RawRgba32;
        Source = TextureImportSourceFactory.CreateRawRgba32InMemory(
            width,
            height,
            colorProfile,
            immutablePayload,
            effectiveIdentity.Value);
    }

    public ResoniteTexturePayload(
        int? width,
        int? height,
        string? colorProfile,
        ITextureImportSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Identity.Value, nameof(source));
        Width = width;
        Height = height;
        ColorProfile = colorProfile;
        BinaryPayload = [];
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
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Identity.Value, nameof(source));
        RawTexturePayload.EnsureValidDimensions(width, height);
        Width = width;
        Height = height;
        ColorProfile = colorProfile;
        BinaryPayload = [];
        Format = ResoniteTexturePayloadFormat.RawRgba32;
        Source = source;
    }

    public int? Width { get; }

    public int? Height { get; }

    public string? ColorProfile { get; }

    public ImmutableArray<byte> BinaryPayload { get; }

    public ResoniteTexturePayloadFormat Format { get; }

    public ITextureImportSource Source { get; }
}
