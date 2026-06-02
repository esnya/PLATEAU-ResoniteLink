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

        ResoniteTexturePayload payload = new(1, 1, "sRGB", source, "dataset:texture");
        source[0] = 9;

        Assert.Equal<byte>([4, 3, 2, 1], payload.BinaryPayload);
    }

    [Fact]
    public void SourceBackedConstructorUsesSourceAsIdentityCarrier()
    {
        FakeTextureImportSource source = new("source:texture");

        ResoniteTexturePayload payload = new(
            width: 1,
            height: 1,
            colorProfile: "sRGB",
            source: source);

        Assert.Same(source, payload.Source);
        Assert.Equal(new TextureImportSourceIdentity("source:texture"), payload.Source.Identity);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void SourceBackedConstructorRejectsBlankSourceIdentity(string identity)
    {
        Assert.Throws<ArgumentException>(() => new ResoniteTexturePayload(
            width: 1,
            height: 1,
            colorProfile: "sRGB",
            source: new FakeTextureImportSource(identity)));
    }

    [Fact]
    public void SourceBackedConstructorRejectsDefaultSourceIdentity()
    {
        Assert.ThrowsAny<ArgumentException>(() => new ResoniteTexturePayload(
            width: 1,
            height: 1,
            colorProfile: "sRGB",
            source: new DefaultIdentityTextureImportSource()));
    }

    private sealed class FakeTextureImportSource(string identity) : ITextureImportSource
    {
        public TextureImportSourceIdentity Identity { get; } = new(identity);

        public string Description => "fake texture";

        public string? ColorProfile => "sRGB";

        public long? EstimatedByteLength => null;
    }

    private sealed class DefaultIdentityTextureImportSource : ITextureImportSource
    {
        public TextureImportSourceIdentity Identity => default;

        public string Description => "fake texture";

        public string? ColorProfile => "sRGB";

        public long? EstimatedByteLength => null;
    }
}
