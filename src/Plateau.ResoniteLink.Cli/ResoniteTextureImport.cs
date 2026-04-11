using Plateau.ResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Plateau.ResoniteLink.Cli;

internal abstract record ResoniteTextureImport;

internal readonly record struct TextureReferenceKey(
    ResoniteTextureSourceKind SourceKind,
    string TexturePath);

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
        string? identity = null,
        string colorProfile = ResoniteTextureColorProfiles.Srgb,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(colorProfile);

        using Image<Rgba32> image = await Image.LoadAsync<Rgba32>(absolutePath, cancellationToken);
        return CreateRawFromImage(image, colorProfile, identity ?? absolutePath);
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

    private static ResoniteRawTextureImport CreateRawFromImage(
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
