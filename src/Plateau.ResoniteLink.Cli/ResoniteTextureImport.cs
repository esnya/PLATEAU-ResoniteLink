using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Plateau.ResoniteLink.Cli;

internal abstract record ResoniteTextureImport;

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
    string? SourcePath = null) : ResoniteTextureImport;

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
        byte[] rawBytes = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(rawBytes);
        return new ResoniteRawTextureImport(
            image.Width,
            image.Height,
            colorProfile,
            rawBytes,
            absolutePath);
    }
}
