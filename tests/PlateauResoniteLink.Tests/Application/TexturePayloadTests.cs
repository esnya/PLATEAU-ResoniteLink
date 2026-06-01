using System;

using PlateauResoniteLink.Application.Importing;

namespace PlateauResoniteLink.Tests.Application;

public sealed class TexturePayloadTests
{
    [Fact]
    public void ConstructorCopiesBinaryPayloadBytes()
    {
        byte[] source = [1, 2, 3, 4];

        TexturePayload payload = new(1, 1, "sRGB", source, "dataset:texture");
        source[0] = 9;

        Assert.Equal<byte>([1, 2, 3, 4], payload.BinaryPayload);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void SourceBackedConstructorRejectsBlankResolvedIdentity(string identity)
    {
        FakeTextureImportSource source = new(identity);

        Assert.Throws<ArgumentException>(() => new TexturePayload(
            width: 1,
            height: 1,
            colorProfile: "sRGB",
            source: source));
        Assert.Throws<ArgumentException>(() => new TexturePayload(
            width: 1,
            height: 1,
            colorProfile: "sRGB",
            source: new FakeTextureImportSource("source:texture"),
            identity: identity));
    }

    private sealed class FakeTextureImportSource(string identity) : ITextureImportSource
    {
        public string Identity { get; } = identity;

        public string Description => "fake texture";

        public string? ColorProfile => "sRGB";

        public long? EstimatedByteLength => null;
    }
}
