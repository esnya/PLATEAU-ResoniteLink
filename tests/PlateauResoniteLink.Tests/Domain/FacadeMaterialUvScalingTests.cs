using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Domain;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class FacadeMaterialUvScalingTests
{
    [Fact]
    public void IsUsableFloorCount_RejectsPlateauUnknownFloorCountSentinel()
    {
        Assert.False(FacadeFloorMetrics.IsUsableFloorCount(FacadeFloorMetrics.UnknownFloorCountSentinel));
    }

    [Theory]
    [InlineData("default-materials/facade/facade001_2k-jpg_color.jpg", 16.0, 10.0, 0.0)]
    [InlineData("default-materials/facade/facade018a_2k-jpg_color.jpg", 6.0, 6.0, 0.5)]
    [InlineData("default-materials/facade/facade019a_2k-jpg_color.jpg", 6.0, 6.0, 0.5)]
    [InlineData("default-materials/facade/facade020a_2k-jpg_color.jpg", 6.0, 6.0, 0.5)]
    public void BundledFacadeProfiles_NormalizeTextureCellToFloorUnitAtMaterialBoundary(
        string texturePath,
        double columnsPerTexture,
        double rowsPerTexture,
        double offsetRows)
    {
        BundledDefaultMaterialProfile profile = BundledDefaultMaterialProfiles.GetProfile(texturePath);

        Assert.Equal(BundledDefaultMaterialUvScaleSemantic.FacadeFloorUnits, profile.ScaleSemantic);
        Assert.Equal(1.0 / columnsPerTexture, profile.TextureScale.X, 6);
        Assert.Equal(1.0 / rowsPerTexture, profile.TextureScale.Y, 6);
        if (offsetRows == 0.0)
        {
            Assert.Null(profile.TextureOffset);
        }
        else
        {
            Assert.NotNull(profile.TextureOffset);
            Assert.Equal(0.0, profile.TextureOffset.X, 6);
            Assert.Equal(offsetRows / rowsPerTexture, profile.TextureOffset.Y, 6);
        }
    }
}
