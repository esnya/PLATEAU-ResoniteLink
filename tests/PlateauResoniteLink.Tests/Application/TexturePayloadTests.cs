using System;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Application;

public sealed class TexturePayloadTests
{
    [Fact]
    public void ConstructorCopiesBinaryPayloadBytes()
    {
        byte[] source = [1, 2, 3, 4];

        RawRgba32TexturePayload payload = new(1, 1, "sRGB", source, "dataset:texture");
        source[0] = 9;

        Assert.Equal<byte>([1, 2, 3, 4], payload.BinaryPayload);
    }

    [Fact]
    public void ConstructorRejectsRawRgbaByteLengthMismatch()
    {
        Assert.Throws<ArgumentException>(
            () => new RawRgba32TexturePayload(2, 2, "sRGB", [255, 255, 255, 255], "dataset:texture"));
    }

    [Fact]
    public void ConstructorRejectsRawRgbaByteLengthOverflow()
    {
        Assert.Throws<OverflowException>(
            () => new RawRgba32TexturePayload(
                int.MaxValue,
                int.MaxValue,
                "sRGB",
                [255, 255, 255, 255],
                "dataset:texture"));
    }

    [Fact]
    public void ConstructorCarriesIdentityAndColorProfileOnSourceOnly()
    {
        RawRgba32TexturePayload payload = new(1, 1, "sRGB", [1, 2, 3, 4], "dataset:texture");

        Assert.Equal("dataset:texture", payload.Source.Identity);
        Assert.Equal("sRGB", payload.Source.ColorProfile);
    }

    [Fact]
    public void ConstructorRejectsTextureSourceWithoutIdentity()
    {
        Assert.Throws<ArgumentException>(
            () => new EncodedImageTexturePayload(null, null, new BlankIdentityTextureImportSource()));
    }

    private sealed class BlankIdentityTextureImportSource : ITextureImportSource
    {
        public string Identity => " ";

        public string Description => "blank";

        public string? ColorProfile => null;

        public long? EstimatedByteLength => null;
    }
}
