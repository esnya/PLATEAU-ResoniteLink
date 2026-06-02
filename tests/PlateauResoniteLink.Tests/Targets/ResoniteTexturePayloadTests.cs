using System;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Targets.Resonite;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class ResoniteTexturePayloadTests
{
    [Fact]
    public void ConstructorCopiesBinaryPayloadBytes()
    {
        byte[] source = [4, 3, 2, 1];

        RawRgba32ResoniteTexturePayload payload = new(1, 1, "sRGB", source, "dataset:texture");
        source[0] = 9;

        Assert.Equal<byte>([4, 3, 2, 1], payload.BinaryPayload);
    }

    [Fact]
    public void ConstructorCarriesIdentityAndColorProfileOnSourceOnly()
    {
        RawRgba32ResoniteTexturePayload payload = new(1, 1, "sRGB", [4, 3, 2, 1], "dataset:texture");

        Assert.Equal("dataset:texture", payload.Source.Identity);
        Assert.Equal("sRGB", payload.Source.ColorProfile);
    }

    [Fact]
    public void ConstructorRejectsTextureSourceWithoutIdentity()
    {
        Assert.Throws<ArgumentException>(
            () => new EncodedImageResoniteTexturePayload(null, null, new BlankIdentityTextureImportSource()));
    }

    private sealed class BlankIdentityTextureImportSource : ITextureImportSource
    {
        public string Identity => " ";

        public string Description => "blank";

        public string? ColorProfile => null;

        public long? EstimatedByteLength => null;
    }
}
