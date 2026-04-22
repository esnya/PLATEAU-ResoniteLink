using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Targets.Resonite;

internal abstract record ResoniteTextureImport;

internal readonly record struct TextureImportCacheKey(
    string Kind,
    string Identity,
    string? ColorProfile = null);

internal static class ResoniteTextureColorProfiles
{
    public const string Linear = "Linear";
    public const string Srgb = "sRGB";
}

internal sealed record ResoniteRawTextureImport(
    int Width,
    int Height,
    string ColorProfile,
    byte[] RawRgba32Bytes,
    string? Identity = null) : ResoniteTextureImport;

internal sealed record ResoniteRawHdrTextureImport(
    int Width,
    int Height,
    byte[] RawRgbaFloatBytes) : ResoniteTextureImport;

internal static class ResoniteTextureImportFactory
{
    public static async Task<ResoniteRawTextureImport> CreateRawFromFileAsync(
        string absolutePath,
        string colorProfile = ResoniteTextureColorProfiles.Srgb,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(colorProfile);

        using Image<Rgba32> image = await Image.LoadAsync<Rgba32>(absolutePath, cancellationToken);
        return CreateRawFromImage(image, colorProfile, absolutePath);
    }

    public static async Task<ResoniteRawTextureImport> CreateRawFromStreamAsync(
        Stream stream,
        string identity,
        string colorProfile = ResoniteTextureColorProfiles.Srgb,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(colorProfile);

        using Image<Rgba32> image = await Image.LoadAsync<Rgba32>(stream, cancellationToken);
        return CreateRawFromImage(image, colorProfile, identity);
    }

    public static ResoniteRawTextureImport CreateRawFromImage(
        Image<Rgba32> image,
        string colorProfile = ResoniteTextureColorProfiles.Srgb,
        string? identity = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(colorProfile);

        return CreateRawFromImageCore(
            image,
            colorProfile,
            identity ?? Guid.NewGuid().ToString("N"));
    }

    public static ResoniteRawTextureImport CreateRawFromPayload(ResoniteTexturePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return payload.Format switch
        {
            ResoniteTexturePayloadFormat.RawRgba32 => CreateRawFromRawPayload(payload),
            ResoniteTexturePayloadFormat.EncodedImage => CreateRawFromEncodedPayload(payload),
            _ => throw new InvalidOperationException($"Unsupported texture payload format '{payload.Format}'."),
        };
    }

    public static ResoniteTexturePayload CreatePayloadFromImage(
        Image<Rgba32> image,
        string colorProfile = ResoniteTextureColorProfiles.Srgb,
        string? identity = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(colorProfile);

        byte[] rawBytes = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(rawBytes);
        return new ResoniteTexturePayload(
            image.Width,
            image.Height,
            colorProfile,
            rawBytes,
            identity ?? Guid.NewGuid().ToString("N"),
            ResoniteTexturePayloadFormat.RawRgba32);
    }

    private static ResoniteRawTextureImport CreateRawFromRawPayload(ResoniteTexturePayload payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload.ColorProfile);
        if (payload.Width is null || payload.Height is null)
        {
            throw new InvalidOperationException("Raw RGBA texture payload must include width and height.");
        }

        return new ResoniteRawTextureImport(
            payload.Width.Value,
            payload.Height.Value,
            payload.ColorProfile,
            payload.CopyBinaryPayloadToArray(),
            payload.Identity);
    }

    private static ResoniteRawTextureImport CreateRawFromEncodedPayload(ResoniteTexturePayload payload)
    {
        using Stream stream = payload.BinaryPayload;
        using Image<Rgba32> image = Image.Load<Rgba32>(stream);
        return CreateRawFromImageCore(
            image,
            payload.ColorProfile ?? ResoniteTextureColorProfiles.Srgb,
            payload.Identity ?? Guid.NewGuid().ToString("N"));
    }

    private static ResoniteRawTextureImport CreateRawFromImageCore(
        Image<Rgba32> image,
        string colorProfile,
        string identity)
    {
        byte[] rawBytes = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(rawBytes);
        return new ResoniteRawTextureImport(
            image.Width,
            image.Height,
            colorProfile,
            rawBytes,
            identity);
    }
}
