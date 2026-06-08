using PlateauResoniteLink.Targets.Resonite;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class ResoniteTexturePayloadTests
{
    [Fact]
    public void CreateUsesCanonicalSrgbColorProfileByDefault()
    {
        using Image<Rgba32> image = new(1, 1);

        RawRgba32ResoniteTexturePayload payload = RawRgba32ResoniteTexturePayload.Create(image);

        Assert.Equal("sRGB", payload.ColorProfile);
        Assert.Equal("sRGB", payload.Source.ColorProfile);
    }

    [Fact]
    public void ConstructorCopiesBinaryPayloadBytes()
    {
        byte[] source = [4, 3, 2, 1];

        RawRgba32ResoniteTexturePayload payload = new(1, 1, "sRGB", source, "dataset:texture");
        source[0] = 9;

        Assert.Equal<byte>([4, 3, 2, 1], payload.BinaryPayload);
    }
}
