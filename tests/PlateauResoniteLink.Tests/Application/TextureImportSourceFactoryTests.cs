using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Application.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Tests.Application;

public sealed class TextureImportSourceFactoryTests
{
    [Fact]
    public async Task CreateInMemoryRawRgbaRequiresDimensionsAtConstruction()
    {
        ITextureImportSource source = TextureImportSourceFactory.CreateInMemoryRaw(
            width: 1,
            height: 1,
            colorProfile: "sRGB",
            bytes: [255, 255, 255, 255],
            identity: "raw-with-dimensions");

        RawTexturePayload payload = await TextureImportSourceMaterializer.MaterializeRawAsync(
            source,
            CancellationToken.None);

        Assert.IsType<Rgba32RawTexturePayload>(payload);
        Assert.Equal(1, payload.Width);
        Assert.Equal(1, payload.Height);
        Assert.Equal([255, 255, 255, 255], payload.Bytes);
    }

    [Fact]
    public async Task CreateInMemoryEncodedImageOwnsDimensionsAfterDecode()
    {
        await using MemoryStream stream = new();
        using (Image<Rgba32> image = new(1, 1, new Rgba32(1, 2, 3, 255)))
        {
            await image.SaveAsPngAsync(stream);
        }

        ITextureImportSource source = TextureImportSourceFactory.CreateInMemoryEncodedImage(
            colorProfile: "sRGB",
            bytes: stream.ToArray(),
            identity: "encoded-without-dimensions");

        RawTexturePayload payload = await TextureImportSourceMaterializer.MaterializeRawAsync(
            source,
            CancellationToken.None);

        Assert.IsType<Rgba32RawTexturePayload>(payload);
        Assert.Equal(1, payload.Width);
        Assert.Equal(1, payload.Height);
        Assert.Equal([1, 2, 3, 255], payload.Bytes);
    }

    [Fact]
    public async Task CreateGeneratedRgbaFloat32ImageDoesNotMaterializeAsRgba32()
    {
        ITextureImportSource source = TextureImportSourceFactory.CreateGeneratedRgbaFloat32Image(
            _ => ValueTask.FromResult(new RgbaFloat32RawTexturePayload(
                width: 1,
                height: 1,
                colorProfile: null,
                bytes: new byte[16])),
            identity: "hdr",
            description: "hdr",
            colorProfile: null,
            estimatedByteLength: 16);

        RawTexturePayload rawPayload = await TextureImportSourceMaterializer.MaterializeRawAsync(
            source,
            CancellationToken.None);
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await TextureImportSourceMaterializer.MaterializeRgba32Async(
                source,
                CancellationToken.None));

        Assert.IsType<RgbaFloat32RawTexturePayload>(rawPayload);
        Assert.Contains("cannot materialize an RGBA32 texture payload", exception.Message, System.StringComparison.Ordinal);
    }

}
