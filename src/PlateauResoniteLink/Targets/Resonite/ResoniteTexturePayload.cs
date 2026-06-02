using System;
using System.Collections.Immutable;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

public abstract class ResoniteTexturePayload
{
    private protected ResoniteTexturePayload(ITextureImportSource source)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        if (string.IsNullOrWhiteSpace(Source.Identity))
        {
            throw new ArgumentException("Texture source identity must be non-empty.", nameof(source));
        }
    }

    public ITextureImportSource Source { get; }
}

public sealed class RawRgba32ResoniteTexturePayload : ResoniteTexturePayload
{
    public RawRgba32ResoniteTexturePayload(
        int width,
        int height,
        string? colorProfile,
        byte[] binaryPayload,
        string? identity = null)
        : this(
            width,
            height,
            colorProfile,
            binaryPayload,
            CreateSource(width, height, colorProfile, binaryPayload, identity))
    {
    }

    private RawRgba32ResoniteTexturePayload(
        int width,
        int height,
        string? colorProfile,
        byte[] binaryPayload,
        ITextureImportSource source)
        : base(source)
    {
        ArgumentNullException.ThrowIfNull(binaryPayload);
        Width = width;
        Height = height;
        BinaryPayload = ImmutableArray.CreateRange(binaryPayload);
    }

    public int Width { get; }

    public int Height { get; }

    public ImmutableArray<byte> BinaryPayload { get; }

    private static ITextureImportSource CreateSource(
        int width,
        int height,
        string? colorProfile,
        byte[] binaryPayload,
        string? identity)
    {
        ArgumentNullException.ThrowIfNull(binaryPayload);
        string effectiveIdentity = identity ?? Guid.NewGuid().ToString("N");
        return TextureImportSourceFactory.CreateInMemoryRaw(
            width,
            height,
            colorProfile,
            binaryPayload,
            effectiveIdentity);
    }
}

public sealed class EncodedImageResoniteTexturePayload : ResoniteTexturePayload
{
    public EncodedImageResoniteTexturePayload(
        int? width,
        int? height,
        ITextureImportSource source)
        : base(source)
    {
        Width = width;
        Height = height;
    }

    public int? Width { get; }

    public int? Height { get; }
}
