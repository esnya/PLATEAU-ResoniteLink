using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Domain;

public sealed class PlateauPackageCatalogTests
{
    [Fact]
    public void MaterialAssignmentPackageListsCoverAllSupportedNonDemPackagesWithoutOverlap()
    {
        string[] categorizedPackages =
        [
            .. PlateauPackageCatalog.BuildingPackageNames,
            .. PlateauPackageCatalog.RoadPackageNames,
            .. PlateauPackageCatalog.WireframeOverlayPackageNames,
            .. PlateauPackageCatalog.VegetationPackageNames,
            .. PlateauPackageCatalog.CityFurniturePackageNames,
            .. PlateauPackageCatalog.OtherMaterialPackageNames,
        ];

        Assert.Equal(
            categorizedPackages.Length,
            categorizedPackages.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        string[] expectedPackages = PlateauPackageCatalog.SupportedPackageNames
            .Where(static packageName => !string.Equals(packageName, "dem", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Equal(
            expectedPackages.OrderBy(static packageName => packageName, StringComparer.OrdinalIgnoreCase),
            categorizedPackages.OrderBy(static packageName => packageName, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void MaterialAssignmentHelpersMatchPackageLists()
    {
        foreach (string packageName in PlateauPackageCatalog.BuildingPackageNames)
        {
            Assert.True(PlateauPackageCatalog.IsBuildingPackage(packageName));
        }

        foreach (string packageName in PlateauPackageCatalog.RoadPackageNames)
        {
            Assert.True(PlateauPackageCatalog.IsRoadPackage(packageName));
        }

        foreach (string packageName in PlateauPackageCatalog.PathLikePackageNames)
        {
            Assert.True(PlateauPackageCatalog.IsPathLikePackage(packageName));
        }

        foreach (string packageName in PlateauPackageCatalog.WireframeOverlayPackageNames)
        {
            Assert.True(PlateauPackageCatalog.IsWireframeOverlayPackage(packageName));
        }

        foreach (string packageName in PlateauPackageCatalog.VegetationPackageNames)
        {
            Assert.True(PlateauPackageCatalog.IsVegetationPackage(packageName));
        }

        foreach (string packageName in PlateauPackageCatalog.CityFurniturePackageNames)
        {
            Assert.True(PlateauPackageCatalog.IsCityFurniturePackage(packageName));
        }

        foreach (string packageName in PlateauPackageCatalog.OtherMaterialPackageNames)
        {
            Assert.True(PlateauPackageCatalog.IsOtherMaterialPackage(packageName));
        }
    }
}
