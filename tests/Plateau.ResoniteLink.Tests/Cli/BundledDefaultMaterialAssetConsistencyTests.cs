using Plateau.ResoniteLink.Cli;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Plateau.ResoniteLink.Tests.Cli;

[Collection(BundledCompanionTextureIsolationGroup.Name)]
public sealed class BundledDefaultMaterialAssetConsistencyTests
{
    private static readonly string[] MetallicLogicalPaths =
    [
        "default-materials/facade/Facade001_2K-JPG_Metallic.png",
        "default-materials/facade/Facade018A_2K-JPG_Metallic.png",
        "default-materials/facade/Facade019A_2K-JPG_Metallic.png",
        "default-materials/facade/Facade020A_2K-JPG_Metallic.png",
        "default-materials/roof/Concrete012_2K-JPG_Metallic.png",
        "default-materials/roof/Concrete033_2K-JPG_Metallic.png",
        "default-materials/roof/RoofingTiles012A_2K-JPG_Metallic.png",
        "default-materials/roof/RoofingTiles014B_2K-JPG_Metallic.png",
        "default-materials/road/Asphalt020L_2K-JPG_Metallic.png",
        "default-materials/road/Asphalt023L_2K-JPG_Metallic.png",
        "default-materials/other/Concrete012_2K-JPG_Metallic.png",
        "default-materials/other/Ground054_2K-JPG_Metallic.png",
        "default-materials/city-furniture/Plaster001_2K-JPG_Metallic.png",
    ];

    private static readonly HashSet<string> FamiliesWithoutAmbientOcclusion = new(StringComparer.Ordinal)
    {
        "default-materials/facade/Facade001_2K-JPG_Metallic.png",
        "default-materials/roof/Concrete012_2K-JPG_Metallic.png",
        "default-materials/other/Concrete012_2K-JPG_Metallic.png",
        "default-materials/city-furniture/Plaster001_2K-JPG_Metallic.png",
    };

    [Fact]
    public void BundledPackedMetallicMapsKeepGreenChannelReservedForOcclusion()
    {
        foreach (string metallicLogicalPath in MetallicLogicalPaths)
        {
            string metallicAbsolutePath = BundledDefaultMaterialAssetStore.GetAbsolutePath(metallicLogicalPath);
            string heightLogicalPath = metallicLogicalPath.Replace("_Metallic.png", "_Height.jpg", StringComparison.Ordinal);
            string heightAbsolutePath = BundledDefaultMaterialAssetStore.GetAbsolutePath(heightLogicalPath);

            using Image<Rgba32> metallicImage = Image.Load<Rgba32>(metallicAbsolutePath);
            using Image<L8> heightImage = Image.Load<L8>(heightAbsolutePath);

            Assert.Equal(heightImage.Width, metallicImage.Width);
            Assert.Equal(heightImage.Height, metallicImage.Height);

            bool greenMatchesHeightEverywhere = true;
            bool greenIsNeutralEverywhere = true;

            for (int y = 0; y < metallicImage.Height; y++)
            {
                for (int x = 0; x < metallicImage.Width; x++)
                {
                    byte green = metallicImage[x, y].G;
                    byte height = heightImage[x, y].PackedValue;

                    greenMatchesHeightEverywhere &= green == height;
                    greenIsNeutralEverywhere &= green == byte.MaxValue;
                }
            }

            Assert.False(
                greenMatchesHeightEverywhere,
                $"Packed metallic map '{metallicLogicalPath}' must not mirror Height into the green occlusion channel.");

            if (FamiliesWithoutAmbientOcclusion.Contains(metallicLogicalPath))
            {
                Assert.True(
                    greenIsNeutralEverywhere,
                    $"Packed metallic map '{metallicLogicalPath}' must keep the green occlusion channel at 255 when no ambient occlusion source exists.");
            }
            else
            {
                Assert.False(
                    greenIsNeutralEverywhere,
                    $"Packed metallic map '{metallicLogicalPath}' should contain bundled ambient occlusion data instead of a neutral green channel.");
            }
        }
    }
}
