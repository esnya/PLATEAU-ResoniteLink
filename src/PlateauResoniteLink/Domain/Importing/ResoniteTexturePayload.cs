using System;
using System.Linq;

namespace PlateauResoniteLink.Domain.Importing;

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
        string? identity = null,
        ResoniteTexturePayloadFormat format = ResoniteTexturePayloadFormat.RawRgba32)
    {
        Width = width;
        Height = height;
        ColorProfile = colorProfile;
        ArgumentNullException.ThrowIfNull(binaryPayload);
        BinaryPayload = binaryPayload.ToArray();
        Identity = identity;
        Format = format;
    }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public string? ColorProfile { get; init; }

    public byte[] BinaryPayload { get; init; }

    public string? Identity { get; init; }

    public ResoniteTexturePayloadFormat Format { get; init; }
}
