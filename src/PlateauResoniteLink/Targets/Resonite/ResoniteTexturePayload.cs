using System;
using System.Collections.Immutable;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Targets.Resonite;

public abstract class ResoniteTexturePayload
{
    private protected ResoniteTexturePayload(
        string? colorProfile,
        string identity,
        ITextureImportSource source)
    {
        if (string.IsNullOrWhiteSpace(identity))
        {
            throw new ArgumentException("Resonite texture payload identity must be provided.", nameof(identity));
        }

        ColorProfile = colorProfile;
        Identity = identity;
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public string? ColorProfile { get; }

    public string Identity { get; }

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
        (string Identity, ITextureImportSource Source) source)
        : base(colorProfile, source.Identity, source.Source)
    {
        ArgumentNullException.ThrowIfNull(binaryPayload);
        Width = width;
        Height = height;
        BinaryPayload = ImmutableArray.CreateRange(binaryPayload);
    }

    public int Width { get; }

    public int Height { get; }

    public ImmutableArray<byte> BinaryPayload { get; }

    private static (string Identity, ITextureImportSource Source) CreateSource(
        int width,
        int height,
        string? colorProfile,
        byte[] binaryPayload,
        string? identity)
    {
        ArgumentNullException.ThrowIfNull(binaryPayload);
        string effectiveIdentity = identity ?? Guid.NewGuid().ToString("N");
        return (
            effectiveIdentity,
            TextureImportSourceFactory.CreateInMemoryRaw(
                width,
                height,
                colorProfile,
                binaryPayload,
                effectiveIdentity));
    }
}

public sealed class EncodedImageResoniteTexturePayload : ResoniteTexturePayload
{
    public EncodedImageResoniteTexturePayload(
        int? width,
        int? height,
        string? colorProfile,
        ITextureImportSource source,
        string? identity = null)
        : this(width, height, colorProfile, CreateSource(source, identity))
    {
    }

    private EncodedImageResoniteTexturePayload(
        int? width,
        int? height,
        string? colorProfile,
        (string Identity, ITextureImportSource Source) source)
        : base(colorProfile, source.Identity, source.Source)
    {
        Width = width;
        Height = height;
    }

    public int? Width { get; }

    public int? Height { get; }

    private static (string Identity, ITextureImportSource Source) CreateSource(
        ITextureImportSource source,
        string? identity)
    {
        ArgumentNullException.ThrowIfNull(source);
        return (identity ?? source.Identity, source);
    }
}
