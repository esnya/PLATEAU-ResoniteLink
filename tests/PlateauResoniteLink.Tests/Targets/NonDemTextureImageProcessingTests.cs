using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class NonDemTextureImageProcessingTests
{
    [Fact]
    public void DetectRepresentativeBackgroundColorAveragesOpaqueBoundaryPixels()
    {
        using Image<Rgba32> image = new(3, 1);
        image[0, 0] = new Rgba32(10, 20, 30, 255);
        image[1, 0] = new Rgba32(200, 210, 220, 255);
        image[2, 0] = new Rgba32(0, 0, 0, 0);

        Rgba32 color = NonDemTextureImageProcessing.DetectRepresentativeBackgroundColor(image);

        Assert.Equal(new Rgba32(105, 115, 125, 255), color);
    }

    [Fact]
    public void FillTransparentRgbBlendsTransparentPixelsTowardBackground()
    {
        using Image<Rgba32> image = new(1, 1);
        image[0, 0] = new Rgba32(100, 50, 0, 128);

        NonDemTextureImageProcessing.FillTransparentRgb(image, new Rgba32(200, 250, 100, 255));

        Assert.Equal(new Rgba32(150, 150, 50, 128), image[0, 0]);
    }

    [Fact]
    public void BakeUsedUvRegionSamplesWrappedUvRegion()
    {
        using Image<Rgba32> image = new(2, 1);
        image[0, 0] = new Rgba32(0, 0, 0, 255);
        image[1, 0] = new Rgba32(255, 0, 0, 255);

        using Image<Rgba32> baked = NonDemTextureImageProcessing.BakeUsedUvRegion(
            image,
            new TextureUvRect(0.5, 0.0, 1.0, 1.0),
            targetWidth: 2,
            targetHeight: 1);

        Assert.Equal(new Rgba32(255, 0, 0, 255), baked[0, 0]);
        Assert.Equal(new Rgba32(0, 0, 0, 255), baked[1, 0]);
    }
}
