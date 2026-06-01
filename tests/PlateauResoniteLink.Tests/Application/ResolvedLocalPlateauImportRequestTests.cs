using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Tests.Application.Importing;

namespace PlateauResoniteLink.Tests.Application;

public sealed class ResolvedLocalPlateauImportRequestTests
{
    [Fact]
    public void CreateCarriesPackageNamesFromValidatedRequest()
    {
        ResolvedLocalPlateauImportRequest request = ResolvedLocalPlateauImportRequestTestFactory.Create(
            packageNames: ["bldg", "wtr"]);

        Assert.Equal(["bldg", "wtr"], request.PackageNames);
    }

    [Fact]
    public void CreateCarriesLocalDemTextureSource()
    {
        ResolvedLocalPlateauImportRequest request = ResolvedLocalPlateauImportRequestTestFactory.Create(
            demTextureLocalSourcePath: "C:\\tmp\\ortho.tif");

        Assert.Equal("C:\\tmp\\ortho.tif", request.DemTextureLocalSourcePath);
    }
}
