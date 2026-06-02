using System;
using System.Threading;
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
    public async Task ConstructorCreatesDimensionedRawTextureSource()
    {
        TexturePayload payload = new(1, 1, "sRGB", [1, 2, 3, 4], "dataset:texture");

        RawTexturePayload rawPayload = await TextureImportSourceMaterializer.MaterializeRawAsync(
            payload.Source,
            CancellationToken.None);

        Assert.Equal(1, rawPayload.Width);
        Assert.Equal(1, rawPayload.Height);
        Assert.Equal<byte>([1, 2, 3, 4], rawPayload.Bytes);
    }

    [Fact]
    public void RawConstructorKeepsGeneratedIdentityConsistentWithSource()
    {
        TexturePayload payload = new(1, 1, "sRGB", [1, 2, 3, 4]);

        Assert.False(string.IsNullOrWhiteSpace(payload.Identity));
        Assert.Equal(payload.Identity, payload.Source.Identity);
    }

    [Theory]
    [InlineData(0, 1, 4)]
    [InlineData(1, 0, 4)]
    [InlineData(1, 1, 3)]
    [InlineData(1, 1, 5)]
    public void RawConstructorRejectsInvalidRawShape(int width, int height, int byteLength)
    {
        byte[] bytes = new byte[byteLength];

        Assert.ThrowsAny<ArgumentException>(() => new TexturePayload(width, height, "sRGB", bytes, "dataset:texture"));
    }

    [Fact]
    public void EncodedConstructorRejectsPayloadIdentityThatDiffersFromSourceIdentity()
    {
        ITextureImportSource source = TextureImportSourceFactory.CreateEncodedImageInMemory(
            "sRGB",
            [1, 2, 3, 4],
            "source:texture");

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new TexturePayload(
                1,
                1,
                "sRGB",
                source,
                "payload:texture"));

        Assert.Contains("identity must match", exception.Message, System.StringComparison.Ordinal);
    }
}
