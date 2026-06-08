using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;

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

                Assert.Equal(BundledDefaultMaterialFamilies.GetVariant(family, variantIndex), variant.Albedo.LogicalPath);
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
        if (!TryGetWallSkinEmissionPath(variant.Albedo.LogicalPath, out string? resolvedEmissionPath))
        {
            return;
        }

        string emissionPath = resolvedEmissionPath!;
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
                        $"Integer V boundary crossed window emission for {variant.Albedo.LogicalPath} at textureV={textureV:0.######}, image row {sampleY}.");
                }
            }
        }
    }

    [Theory]
    [InlineData(BundledDefaultMaterialFamilies.WallResidentialPlasterLow, 1, "default-materials/wallskins/wall_res_plaster_low/emission.png")]
    [InlineData(BundledDefaultMaterialFamilies.WallResidentialTileLow, 1, "default-materials/wallskins/wall_res_tile_low/emission.png")]
    [InlineData(BundledDefaultMaterialFamilies.WallResidentialTileLow, 2, "default-materials/wallskins/wall_res_plaster_low/emission.png")]
    [InlineData(BundledDefaultMaterialFamilies.WallResidentialTileLow, 3, "default-materials/wallskins/wall_res_plaster_low/emission.png")]
    public void ColorVariantEmissionCanShareStableTextureSource(
        string family,
        int variantIndex,
        string expectedEmissionTexturePath)
    {
        BundledDefaultMaterialVariant variant = BundledDefaultMaterialFamilies.GetVariantDefinition(family, variantIndex);

        Assert.NotNull(variant.TextureSources);
        Assert.Equal(expectedEmissionTexturePath, variant.TextureSources.Emission?.LogicalPath);
        Assert.Null(variant.TextureSources.Albedo);
        if (family == BundledDefaultMaterialFamilies.WallResidentialPlasterLow && variantIndex == 1)
        {
            Assert.Equal(
                BundledDefaultTextureAssets.WallSkins.ResidentialPlasterLow.Metallic.LogicalPath,
                variant.TextureSources.Metallic?.LogicalPath);
        }
        else
        {
            Assert.NotNull(variant.TextureSources.Metallic);
        }
        Assert.NotNull(variant.TextureSources.Height);
        Assert.NotNull(variant.TextureSources.Normal);
    }

    [Theory]
    [InlineData(BundledDefaultMaterialFamilies.Facade, 1)]
    [InlineData(BundledDefaultMaterialFamilies.Facade, 2)]
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
    public void BlackEmissionColorVariantsDoNotDefineSharedTextureSource(string family, int variantIndex)
    {
        BundledDefaultMaterialVariant variant = BundledDefaultMaterialFamilies.GetVariantDefinition(family, variantIndex);

        Assert.Null(variant.TextureSources?.Emission);
    }

    [Fact]
    public void MaterialVariantTextureSourcesUseTypedAssetsInsteadOfPaths()
    {
        Type[] constructorParameterTypes = typeof(BundledDefaultMaterialTextureSources)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(static parameter => parameter.ParameterType)
            .ToArray();

        Assert.All(constructorParameterTypes, static type => Assert.NotEqual(typeof(string), Nullable.GetUnderlyingType(type) ?? type));
    }

    [Fact]
    public void BundledDefaultMaterialAssetStoreReadsBundledResourceWithoutFileMaterialization()
    {
        string logicalPath = BundledDefaultMaterialFamilies.GetVariant(BundledDefaultMaterialFamilies.CityFurniture, 0);
        BundledDefaultMaterialAssetStore assetStore = new();

        using Stream stream = assetStore.OpenRead(logicalPath);

        using Image image = Image.Load(stream);
        Assert.True(image.Width > 0);
        Assert.True(image.Height > 0);
    }

    [Theory]
    [InlineData("default-materials/ambientcg/facade/Facade018A_2K-JPG_Emission.jpg")]
    [InlineData("default-materials/ambientcg/facade/Facade019A_2K-JPG_Emission.jpg")]
    [InlineData("default-materials/ambientcg/facade/Facade020A_2K-JPG_Emission.jpg")]
    [InlineData("default-materials/wallskins/wall_apartment_tile_dark/emission.png")]
    [InlineData("default-materials/wallskins/wall_apartment_tile_mid/emission.png")]
    [InlineData("default-materials/wallskins/wall_brick_dark/emission.png")]
    [InlineData("default-materials/wallskins/wall_brick_retro/emission.png")]
    [InlineData("default-materials/wallskins/wall_commercial_panel/emission.png")]
    [InlineData("default-materials/wallskins/wall_commercial_panel_dark/emission.png")]
    [InlineData("default-materials/wallskins/wall_factory_metal/emission.png")]
    [InlineData("default-materials/wallskins/wall_rc_painted_dark/emission.png")]
    [InlineData("default-materials/wallskins/wall_rc_painted_mid/emission.png")]
    [InlineData("default-materials/wallskins/wall_school_public_band/emission.png")]
    [InlineData("default-materials/wallskins/wall_school_public_dark/emission.png")]
    public void KnownBlackEmissionImagesAreNotBundledSources(string logicalPath)
    {
        Assert.False(new BundledDefaultMaterialAssetStore().Contains(logicalPath));
    }

    [Theory]
    [InlineData("default-materials/ambientcg/facade/Facade002_2K-JPG_Height.jpg")]
    [InlineData("default-materials/ambientcg/facade/Facade002_2K-JPG_Metallic.png")]
    [InlineData("default-materials/ambientcg/facade/Facade002_2K-JPG_NormalGL.jpg")]
    [InlineData("default-materials/ambientcg/roof/Concrete012_2K-JPG_Color.jpg")]
    [InlineData("default-materials/ambientcg/roof/Concrete012_2K-JPG_Height.jpg")]
    [InlineData("default-materials/ambientcg/roof/Concrete012_2K-JPG_Metallic.png")]
    [InlineData("default-materials/ambientcg/roof/Concrete012_2K-JPG_NormalGL.jpg")]
    [InlineData("default-materials/wallskins/wall_res_plaster_dark/emission.png")]
    [InlineData("default-materials/wallskins/wall_res_siding_brick_gray/emission.png")]
    [InlineData("default-materials/wallskins/wall_res_tile_dark/emission.png")]
    [InlineData("default-materials/wallskins/wall_res_tile_dark_irregular/emission.png")]
    [InlineData("default-materials/wallskins/wall_res_plaster_dark/metallic_ao_smoothness.png")]
    [InlineData("default-materials/ambientcg/facade/Facade015_2K-JPG_Height.jpg")]
    [InlineData("default-materials/ambientcg/facade/Facade015_2K-JPG_Metallic.png")]
    [InlineData("default-materials/ambientcg/facade/Facade015_2K-JPG_NormalGL.jpg")]
    [InlineData("default-materials/ambientcg/roof/Asphalt025B_2K-JPG_Height.jpg")]
    [InlineData("default-materials/ambientcg/roof/Asphalt025C_2K-JPG_Height.jpg")]
    [InlineData("default-materials/ambientcg/roof/Asphalt025B_2K-JPG_NormalGL.jpg")]
    [InlineData("default-materials/ambientcg/roof/Asphalt025C_2K-JPG_NormalGL.jpg")]
    public void SharedDuplicateTextureImagesAreNotBundledSources(string logicalPath)
    {
        Assert.False(new BundledDefaultMaterialAssetStore().Contains(logicalPath));
    }

    [Theory]
    [InlineData("default-materials/texturecan/facade/Others0021_2K_Height.png")]
    [InlineData("default-materials/texturecan/facade/Others0022_2K_Height.png")]
    [InlineData("default-materials/texturecan/facade/Others0025_2K_Height.png")]
    [InlineData("default-materials/texturecan/facade/Others0026_2K_Height.png")]
    [InlineData("default-materials/texturecan/facade/Others0029_2K_Height.png")]
    public void FlatWhiteTextureCanHeightMapsAreNotBundledSources(string logicalPath)
    {
        Assert.False(new BundledDefaultMaterialAssetStore().Contains(logicalPath));
    }

    private static bool TryGetWallSkinEmissionPath(string texturePath, out string? emissionPath)
    {
        const string prefix = "default-materials/wallskins/";
        const string suffix = "/basecolor.png";
        Assert.StartsWith(prefix, texturePath, StringComparison.Ordinal);
        Assert.EndsWith(suffix, texturePath, StringComparison.Ordinal);
        string materialName = texturePath[prefix.Length..^suffix.Length];
        string logicalPath = $"default-materials/wallskins/{materialName}/emission.png";
        if (!new BundledDefaultMaterialAssetStore().Contains(logicalPath))
        {
            emissionPath = null;
            return false;
        }

        emissionPath = TestData.GetRepositoryPath(
            "src",
            "PlateauResoniteLink",
            "Assets",
            "DefaultMaterials",
            "wallskins",
            materialName,
            "emission.png");
        return true;
    }

}
