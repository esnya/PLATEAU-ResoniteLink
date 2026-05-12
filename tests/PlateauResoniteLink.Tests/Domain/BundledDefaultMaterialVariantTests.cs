using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Domain;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class BundledDefaultMaterialVariantTests
{
    [Fact]
    public void IsUsableFloorCount_RejectsPlateauUnknownFloorCountSentinel()
    {
        Assert.False(FacadeFloorMetrics.IsUsableFloorCount(FacadeFloorMetrics.UnknownFloorCountSentinel));
    }

    [Theory]
    [InlineData(BundledDefaultMaterialFamilies.FacadeHighriseGlass, 0, 16.0, 10.0, 0.0)]
    [InlineData(BundledDefaultMaterialFamilies.FacadeHighriseNightLow, 0, 16.0, 10.0, 0.0)]
    [InlineData(BundledDefaultMaterialFamilies.FacadeHighriseGlass, 1, 32.0, 24.0, 0.0)]
    [InlineData(BundledDefaultMaterialFamilies.FacadeHighriseGlass, 2, 14.0, 8.0, 0.0)]
    [InlineData(BundledDefaultMaterialFamilies.FacadeHighriseNightLow, 1, 40.0, 40.0, 0.0)]
    [InlineData(BundledDefaultMaterialFamilies.FacadeMidriseGrid, 0, 32.0, 32.0, 0.25)]
    [InlineData(BundledDefaultMaterialFamilies.FacadeMidriseGrid, 1, 32.0, 32.0, 0.25)]
    [InlineData(BundledDefaultMaterialFamilies.Facade, 0, 6.0, 6.0, 0.5)]
    [InlineData(BundledDefaultMaterialFamilies.Facade, 1, 6.0, 6.0, 0.5)]
    [InlineData(BundledDefaultMaterialFamilies.Facade, 2, 6.0, 6.0, 0.5)]
    public void BundledFacadeVariantsCarryFloorUnitTextureSetBesideTexturePath(
        string family,
        int variantIndex,
        double columnsPerTexture,
        double rowsPerTexture,
        double offsetRows)
    {
        BundledDefaultMaterialVariant variant = BundledDefaultMaterialFamilies.GetVariantDefinition(family, variantIndex);
        BundledDefaultMaterialProfile textureSet = variant.TextureSet;

        Assert.Equal(BundledDefaultMaterialFamilies.GetVariant(family, variantIndex), variant.TexturePath);
        Assert.Equal(BundledDefaultMaterialUvScaleSemantic.FacadeFloorUnits, textureSet.ScaleSemantic);
        Assert.True(columnsPerTexture > 0.0);
        if (columnsPerTexture != rowsPerTexture)
        {
            Assert.NotEqual(1.0 / columnsPerTexture, textureSet.TextureScale.X, 6);
        }

        Assert.Equal(1.0 / rowsPerTexture, textureSet.TextureScale.X, 6);
        Assert.Equal(1.0 / rowsPerTexture, textureSet.TextureScale.Y, 6);
        if (offsetRows == 0.0)
        {
            Assert.Null(textureSet.TextureOffset);
        }
        else
        {
            Assert.NotNull(textureSet.TextureOffset);
            Assert.Equal(0.0, textureSet.TextureOffset.X, 6);
            Assert.Equal(offsetRows / rowsPerTexture, textureSet.TextureOffset.Y, 6);
        }
    }
}
