using System;
using System.Collections.Immutable;

using PlateauResoniteLink.Core.Application.Importing.Contracts;


namespace PlateauResoniteLink.Resonite.Targets.Resonite;

public abstract class ResoniteTexturePayload
{
    private protected ResoniteTexturePayload(
        string? colorProfile,
        ITextureImportSource source)
    {
        ColorProfile = colorProfile;
        Source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public string? ColorProfile { get; }

    public ITextureImportSource Source { get; }
}

public sealed class RawRgba32ResoniteTexturePayload : ResoniteTexturePayload
{
    public RawRgba32ResoniteTexturePayload(
        int width,
        int height,
        string? colorProfile,
        byte[] binaryPayload,
        string? description = null)
        : this(
            width,
            height,
            colorProfile,
            binaryPayload,
            CreateSource(width, height, colorProfile, binaryPayload, description))
    {
    }

    private RawRgba32ResoniteTexturePayload(
        int width,
        int height,
        string? colorProfile,
        byte[] binaryPayload,
        ITextureImportSource source)
        : base(colorProfile, source)
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
        string? description)
    {
        ArgumentNullException.ThrowIfNull(binaryPayload);
        return TextureImportSourceFactory.CreateInMemoryRaw(
            width,
            height,
            colorProfile,
            binaryPayload,
            description ?? "memory:raw-rgba32");
    }
}

public sealed class EncodedImageResoniteTexturePayload : ResoniteTexturePayload
{
    public EncodedImageResoniteTexturePayload(
        int? width,
        int? height,
        string? colorProfile,
        ITextureImportSource source,
        string? description = null)
        : base(colorProfile, source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _ = description;
        Width = width;
        Height = height;
    }

    public int? Width { get; }

    public int? Height { get; }
}
