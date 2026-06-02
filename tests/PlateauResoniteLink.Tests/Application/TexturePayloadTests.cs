using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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
    public async Task RawSourceMaterializationReusesImmutablePayloadBytes()
    {
        TexturePayload payload = new(1, 1, "sRGB", [1, 2, 3, 4], "dataset:texture");

        RawTexturePayload first = await TextureImportSourceMaterializer.MaterializeRawAsync(
            payload.Source,
            CancellationToken.None);
        RawTexturePayload second = await TextureImportSourceMaterializer.MaterializeRawAsync(
            payload.Source,
            CancellationToken.None);

        Assert.Same(
            ImmutableCollectionsMarshal.AsArray(first.Bytes),
            ImmutableCollectionsMarshal.AsArray(second.Bytes));
    }

    [Fact]
    public void RawConstructorKeepsGeneratedIdentityConsistentWithSource()
    {
        TexturePayload payload = new(1, 1, "sRGB", [1, 2, 3, 4]);

        Assert.False(string.IsNullOrWhiteSpace(payload.Identity));
        Assert.Equal(payload.Identity, payload.Source.Identity);
    }

    [Fact]
    public async Task EncodedSourceCopiesInputBeforeMaterialization()
    {
        byte[] sourceBytes = CreateEncodedPixelBytes(new Rgba32(10, 20, 30, 255));
        ITextureImportSource source = TextureImportSourceFactory.CreateEncodedImageInMemory(
            "sRGB",
            sourceBytes,
            "dataset:encoded-texture");
        Array.Fill<byte>(sourceBytes, 0);

        RawTexturePayload rawPayload = await TextureImportSourceMaterializer.MaterializeRawAsync(
            source,
            CancellationToken.None);

        Assert.Equal(1, rawPayload.Width);
        Assert.Equal(1, rawPayload.Height);
        Assert.Equal<byte>([10, 20, 30, 255], rawPayload.Bytes);
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

    private static byte[] CreateEncodedPixelBytes(Rgba32 pixel)
    {
        using Image<Rgba32> image = new(1, 1);
        image[0, 0] = pixel;
        using MemoryStream stream = new();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }
}
