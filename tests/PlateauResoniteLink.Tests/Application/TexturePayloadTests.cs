using PlateauResoniteLink.Core.Application.Importing.Contracts;

using System;


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
    public void ConstructorCarriesDescriptionAndColorProfileOnSource()
    {
        RawRgba32TexturePayload payload = new(1, 1, "sRGB", [1, 2, 3, 4], "dataset:texture");

        Assert.Equal("sRGB", payload.ColorProfile);
        Assert.Equal("dataset:texture", payload.Source.Description);
        Assert.Equal("sRGB", payload.Source.ColorProfile);
    }

    [Fact]
    public void ConstructorRequiresTextureSource()
    {
        Assert.Throws<ArgumentNullException>(
            () => new EncodedImageTexturePayload(null, null, null, null!));
    }
}
