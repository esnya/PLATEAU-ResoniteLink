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
    public void CreateInMemoryRawRgbaRequiresDimensionsBeforeMaterialization()
    {
        ArgumentException widthError = Assert.Throws<ArgumentException>(
            () => TextureImportSourceFactory.CreateInMemory(
                width: null,
                height: 1,
                colorProfile: "sRGB",
                bytes: [255, 255, 255, 255],
                identity: "raw-without-width",
                sourceFormat: TexturePayloadFormat.RawRgba32));
        ArgumentException heightError = Assert.Throws<ArgumentException>(
            () => TextureImportSourceFactory.CreateInMemory(
                width: 1,
                height: null,
                colorProfile: "sRGB",
                bytes: [255, 255, 255, 255],
                identity: "raw-without-height",
                sourceFormat: TexturePayloadFormat.RawRgba32));

        Assert.Equal("width", widthError.ParamName);
        Assert.Equal("height", heightError.ParamName);
    }

    [Fact]
    public async Task CreateInMemoryEncodedImageOwnsDimensionsAfterDecode()
    {
        await using MemoryStream stream = new();
        using (Image<Rgba32> image = new(1, 1, new Rgba32(1, 2, 3, 255)))
        {
            await image.SaveAsPngAsync(stream);
        }

        ITextureImportSource source = TextureImportSourceFactory.CreateInMemory(
            width: null,
            height: null,
            colorProfile: "sRGB",
            bytes: stream.ToArray(),
            identity: "encoded-without-dimensions",
            sourceFormat: TexturePayloadFormat.EncodedImage);

        RawTexturePayload payload = await TextureImportSourceMaterializer.MaterializeRawAsync(
            source,
            CancellationToken.None);

        Assert.Equal(1, payload.Width);
        Assert.Equal(1, payload.Height);
        Assert.Equal([1, 2, 3, 255], payload.Bytes);
    }

    [Fact]
    public void TexturePayloadRejectsUnsupportedFormatAtConstructionBoundary()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TexturePayload(
                width: 1,
                height: 1,
                colorProfile: "sRGB",
                binaryPayload: [255, 255, 255, 255],
                identity: "invalid-format",
                format: (TexturePayloadFormat)999));
    }
}
