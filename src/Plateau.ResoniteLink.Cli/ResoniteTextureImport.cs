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
    public static ResoniteFileTextureImport CreateFromFile(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        return new ResoniteFileTextureImport(absolutePath);
    }
}
