namespace PlateauResoniteLink.Targets.Resonite;

public enum ResoniteTexturePayloadFormat
{
    RawRgba32,
    EncodedImage,
}

public sealed record ResoniteTexturePayload(
    int? Width,
    int? Height,
    string? ColorProfile,
    byte[] BinaryPayload,
    string? Identity = null,
    ResoniteTexturePayloadFormat Format = ResoniteTexturePayloadFormat.RawRgba32);
