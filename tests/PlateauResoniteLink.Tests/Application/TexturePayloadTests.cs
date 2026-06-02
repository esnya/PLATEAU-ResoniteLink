using System;
using System.Threading.Tasks;

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

    [Fact]
    public void SourceBackedConstructorUsesSourceAsIdentityCarrier()
    {
        FakeTextureImportSource source = new("source:texture");

        TexturePayload payload = new(
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
        Assert.Throws<ArgumentException>(() => new TexturePayload(
            width: 1,
            height: 1,
            colorProfile: "sRGB",
            source: new FakeTextureImportSource(identity)));
    }

    [Fact]
    public void SourceBackedConstructorRejectsDefaultSourceIdentity()
    {
        Assert.ThrowsAny<ArgumentException>(() => new TexturePayload(
            width: 1,
            height: 1,
            colorProfile: "sRGB",
            source: new DefaultIdentityTextureImportSource()));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void TextureImportSourceFactoryRejectsBlankSourceIdentities(string identity)
    {
        Assert.Throws<ArgumentException>(() => TextureImportSourceFactory.CreateDatasetEncodedImage(
            null!,
            "textures/albedo.png",
            "sRGB",
            identity));
        Assert.Throws<ArgumentException>(() => TextureImportSourceFactory.CreateFileImage(
            "textures/albedo.png",
            "sRGB",
            identity));
        Assert.Throws<ArgumentException>(() => TextureImportSourceFactory.CreateGeneratedImage(
            static _ => ValueTask.FromResult(new RawTexturePayload(1, 1, "sRGB", [0, 0, 0, 255])),
            identity,
            "generated",
            "sRGB"));
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
