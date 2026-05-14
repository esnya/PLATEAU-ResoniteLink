using System;
using System.Collections.Generic;

using PlateauResoniteLink.Domain.Importing;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PlateauResoniteLink.Tests.Domain;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class BundledDefaultMaterialVariantTests
{
    [Fact]
    public void IsUsableFloorCount_RejectsPlateauUnknownFloorCountSentinel()
    {
        Assert.False(FacadeFloorMetrics.IsUsableFloorCount(FacadeFloorMetrics.UnknownFloorCountSentinel));
    }

    [Fact]
    public void BundledFacadeVariantsCarryFloorUnitTextureSetBesideTexturePath()
    {
        foreach (string family in BundledDefaultMaterialFamilies.BuildingFacadeFamilies)
        {
            IReadOnlyList<BundledDefaultMaterialVariant> variants = BundledDefaultMaterialFamilies.GetVariantDefinitions(family);
            for (int variantIndex = 0; variantIndex < variants.Count; variantIndex++)
            {
                BundledDefaultMaterialVariant variant = variants[variantIndex];
                BundledDefaultMaterialProfile textureSet = variant.TextureSet;

                Assert.Equal(BundledDefaultMaterialFamilies.GetVariant(family, variantIndex), variant.TexturePath);
                Assert.Equal(BundledDefaultMaterialUvScaleSemantic.FacadeFloorUnits, textureSet.ScaleSemantic);
                Assert.True(textureSet.TextureScale.X > 0.0);
                Assert.True(textureSet.TextureScale.Y > 0.0);
                Assert.Equal(textureSet.TextureScale.X, textureSet.TextureScale.Y, 6);
            }
        }
    }

    [Theory]
    [InlineData(BundledDefaultMaterialFamilies.WallResidentialPlasterLow, 0, 3.0)]
    [InlineData(BundledDefaultMaterialFamilies.WallResidentialPlasterLow, 1, 3.0)]
    [InlineData(BundledDefaultMaterialFamilies.WallResidentialTileLow, 0, 3.0)]
    [InlineData(BundledDefaultMaterialFamilies.WallResidentialTileLow, 1, 3.0)]
    [InlineData(BundledDefaultMaterialFamilies.WallResidentialTileLow, 2, 3.0)]
    [InlineData(BundledDefaultMaterialFamilies.WallResidentialTileLow, 3, 3.0)]
    [InlineData(BundledDefaultMaterialFamilies.WallApartmentTileMid, 0, 6.0)]
    [InlineData(BundledDefaultMaterialFamilies.WallApartmentTileMid, 1, 6.0)]
    [InlineData(BundledDefaultMaterialFamilies.WallRcPaintedMid, 0, 5.0)]
    [InlineData(BundledDefaultMaterialFamilies.WallRcPaintedMid, 1, 5.0)]
    [InlineData(BundledDefaultMaterialFamilies.WallFactoryMetal, 0, 2.0)]
    [InlineData(BundledDefaultMaterialFamilies.WallCommercialPanel, 0, 5.0)]
    [InlineData(BundledDefaultMaterialFamilies.WallCommercialPanel, 1, 5.0)]
    [InlineData(BundledDefaultMaterialFamilies.WallSchoolPublicBand, 0, 4.0)]
    [InlineData(BundledDefaultMaterialFamilies.WallSchoolPublicBand, 1, 4.0)]
    [InlineData(BundledDefaultMaterialFamilies.WallBrickRetro, 0, 4.0)]
    [InlineData(BundledDefaultMaterialFamilies.WallBrickRetro, 1, 4.0)]
    [InlineData(BundledDefaultMaterialFamilies.WallWoodRural, 0, 2.0)]
    public void WallSkinFacadeVariantsUseOnePaddingRowAndHalfFloorPhase(
        string family,
        int variantIndex,
        double storeysInTexture)
    {
        double repeatRows = storeysInTexture + 1.0;

        BundledDefaultMaterialProfile textureSet = BundledDefaultMaterialFamilies
            .GetVariantDefinition(family, variantIndex)
            .TextureSet;

        Assert.Equal(1.0 / repeatRows, textureSet.TextureScale.X, 12);
        Assert.Equal(1.0 / repeatRows, textureSet.TextureScale.Y, 12);
        ScalarPair textureOffset = Assert.IsType<ScalarPair>(textureSet.TextureOffset);
        Assert.Equal(0.0, textureOffset.X, 12);
        Assert.Equal(0.5 / repeatRows, textureOffset.Y, 12);

        for (int floorBoundary = 0; floorBoundary <= storeysInTexture; floorBoundary++)
        {
            double rowPosition = (textureOffset.Y + (floorBoundary * textureSet.TextureScale.Y)) * repeatRows;
            Assert.Equal(floorBoundary + 0.5, rowPosition, 12);
        }
    }

    [Theory]
    [InlineData(BundledDefaultMaterialFamilies.WallResidentialPlasterLow, 0)]
    [InlineData(BundledDefaultMaterialFamilies.WallResidentialPlasterLow, 1)]
    [InlineData(BundledDefaultMaterialFamilies.WallResidentialTileLow, 0)]
    [InlineData(BundledDefaultMaterialFamilies.WallResidentialTileLow, 1)]
    [InlineData(BundledDefaultMaterialFamilies.WallResidentialTileLow, 2)]
    [InlineData(BundledDefaultMaterialFamilies.WallResidentialTileLow, 3)]
    [InlineData(BundledDefaultMaterialFamilies.WallApartmentTileMid, 0)]
    [InlineData(BundledDefaultMaterialFamilies.WallApartmentTileMid, 1)]
    [InlineData(BundledDefaultMaterialFamilies.WallRcPaintedMid, 0)]
    [InlineData(BundledDefaultMaterialFamilies.WallRcPaintedMid, 1)]
    [InlineData(BundledDefaultMaterialFamilies.WallFactoryMetal, 0)]
    [InlineData(BundledDefaultMaterialFamilies.WallCommercialPanel, 0)]
    [InlineData(BundledDefaultMaterialFamilies.WallCommercialPanel, 1)]
    [InlineData(BundledDefaultMaterialFamilies.WallSchoolPublicBand, 0)]
    [InlineData(BundledDefaultMaterialFamilies.WallSchoolPublicBand, 1)]
    [InlineData(BundledDefaultMaterialFamilies.WallBrickRetro, 0)]
    [InlineData(BundledDefaultMaterialFamilies.WallBrickRetro, 1)]
    [InlineData(BundledDefaultMaterialFamilies.WallWoodRural, 0)]
    public void WallSkinFacadeIntegerVBoundariesDoNotCrossWindowEmission(
        string family,
        int variantIndex)
    {
        BundledDefaultMaterialVariant variant = BundledDefaultMaterialFamilies.GetVariantDefinition(family, variantIndex);
        BundledDefaultMaterialProfile textureSet = variant.TextureSet;
        ScalarPair textureOffset = Assert.IsType<ScalarPair>(textureSet.TextureOffset);
        string emissionPath = GetWallSkinEmissionPath(variant.TexturePath);

        using Image<Rgba32> emission = Image.Load<Rgba32>(emissionPath);
        for (double textureV = textureOffset.Y; textureV <= 1.0 + 1e-12; textureV += textureSet.TextureScale.Y)
        {
            int y = (int)Math.Round((1.0 - textureV) * (emission.Height - 1));
            int minY = Math.Max(0, y - 1);
            int maxY = Math.Min(emission.Height - 1, y + 1);
            for (int sampleY = minY; sampleY <= maxY; sampleY++)
            {
                for (int x = 0; x < emission.Width; x++)
                {
                    Rgba32 pixel = emission[x, sampleY];
                    Assert.True(
                        Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) <= 10,
                        $"Integer V boundary crossed window emission for {variant.TexturePath} at textureV={textureV:0.######}, image row {sampleY}.");
                }
            }
        }
    }

    private static string GetWallSkinEmissionPath(string texturePath)
    {
        const string prefix = "default-materials/wallskins/";
        const string suffix = "/basecolor.png";
        Assert.StartsWith(prefix, texturePath, StringComparison.Ordinal);
        Assert.EndsWith(suffix, texturePath, StringComparison.Ordinal);
        string materialName = texturePath[prefix.Length..^suffix.Length];
        return TestData.GetRepositoryPath(
            "src",
            "PlateauResoniteLink",
            "Assets",
            "DefaultMaterials",
            "wallskins",
            materialName,
            "emission.png");
    }
}
