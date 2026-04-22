using System;
using System.IO;

namespace PlateauResoniteLink.Domain.Importing;

public enum ResoniteTexturePayloadFormat
{
    RawRgba32,
    EncodedImage,
}

public sealed record ResoniteTexturePayload
{
    private readonly byte[] binaryPayloadBytes;

    public ResoniteTexturePayload(
        int? Width,
        int? Height,
        string? ColorProfile,
        Stream BinaryPayload,
        string? Identity = null,
        ResoniteTexturePayloadFormat Format = ResoniteTexturePayloadFormat.RawRgba32)
    {
        this.Width = Width;
        this.Height = Height;
        this.ColorProfile = ColorProfile;
        binaryPayloadBytes = CopyBinaryPayload(BinaryPayload);
        this.Identity = Identity;
        this.Format = Format;
    }

    public ResoniteTexturePayload(
        int? Width,
        int? Height,
        string? ColorProfile,
        byte[] BinaryPayload,
        string? Identity = null,
        ResoniteTexturePayloadFormat Format = ResoniteTexturePayloadFormat.RawRgba32)
        : this(Width, Height, ColorProfile, CreateBinaryPayloadStream(BinaryPayload), Identity, Format)
    {
    }

    public int? Width { get; }

    public int? Height { get; }

    public string? ColorProfile { get; }

    public Stream BinaryPayload => CreateBinaryPayloadStream(binaryPayloadBytes);

    public string? Identity { get; }

    public ResoniteTexturePayloadFormat Format { get; }

    internal byte[] CopyBinaryPayloadToArray() => (byte[])binaryPayloadBytes.Clone();

    private static MemoryStream CreateBinaryPayloadStream(byte[] binaryPayload)
    {
        ArgumentNullException.ThrowIfNull(binaryPayload);
        return new MemoryStream(binaryPayload, writable: false);
    }

    private static byte[] CopyBinaryPayload(Stream binaryPayload)
    {
        ArgumentNullException.ThrowIfNull(binaryPayload);
        if (!binaryPayload.CanRead)
        {
            throw new ArgumentException("Texture payload stream must be readable.", nameof(binaryPayload));
        }

        long originalPosition = 0;
        bool restorePosition = binaryPayload.CanSeek;
        if (restorePosition)
        {
            originalPosition = binaryPayload.Position;
        }

        try
        {
            using MemoryStream copy = new();
            binaryPayload.CopyTo(copy);
            return copy.ToArray();
        }
        finally
        {
            if (restorePosition)
            {
                binaryPayload.Position = originalPosition;
            }
        }
    }
}
