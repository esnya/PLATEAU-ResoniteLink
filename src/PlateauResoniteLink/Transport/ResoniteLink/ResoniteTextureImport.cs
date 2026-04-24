namespace PlateauResoniteLink.Transport.ResoniteLink;

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
    byte[] RawRgba32Bytes) : ResoniteTextureImport;

internal sealed record ResoniteRawHdrTextureImport(
    int Width,
    int Height,
    byte[] RawRgbaFloatBytes) : ResoniteTextureImport;
