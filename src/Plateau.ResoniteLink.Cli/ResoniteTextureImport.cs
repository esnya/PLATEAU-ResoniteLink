using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Plateau.ResoniteLink.Cli;

internal abstract record ResoniteTextureImport;

internal sealed record ResoniteFileTextureImport(string AbsolutePath) : ResoniteTextureImport;

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
    public static ResoniteRawTextureImport CreateFromFile(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        using Image<Rgba32> image = Image.Load<Rgba32>(absolutePath);
        byte[] rawBytes = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(rawBytes);
        return new ResoniteRawTextureImport(image.Width, image.Height, "sRGB", rawBytes, absolutePath);
    }
}
