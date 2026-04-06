using Plateau.ResoniteLink.Application.Importing;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class LocalCityGmlSourceFileDiscoveryTests
{
    [Fact]
    public void DiscoverOrdersDemBeforeOtherPackages()
    {
        string datasetRoot = TestData.GetFixturePath("LocalPlateauDatasetMixedObjects");

        IReadOnlyList<LocalCityGmlSourceFileDescriptor> result = LocalCityGmlSourceFileDiscovery.Discover(
            datasetRoot,
            "53394525",
            packageNames: null);

        Assert.Equal(["dem", "bldg", "luse", "tran"], result.Select(static file => file.PackageName).ToArray());
        Assert.Equal(
            "udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml",
            result[0].RelativePath);
        Assert.All(result, static file => Assert.False(file.RequiresMeshAreaFilter));
    }

    [Fact]
    public void DiscoverFiltersPackagesAndKeepsParentMeshMatches()
    {
        string datasetRoot = TestData.GetFixturePath("LocalPlateauDatasetParentMeshPackages");

        IReadOnlyList<LocalCityGmlSourceFileDescriptor> result = LocalCityGmlSourceFileDiscovery.Discover(
            datasetRoot,
            "53394525",
            ["waterbody", "tran", "dem"]);

        Assert.Equal(["dem", "tran"], result.Select(static file => file.PackageName).ToArray());
        Assert.Contains(
            result,
            static file =>
                file.RelativePath == "udx/dem/533945/plateau_tokyo23ku_dem_533945.gml"
                && file.RequiresMeshAreaFilter);
        Assert.Contains(
            result,
            static file =>
                file.RelativePath == "udx/tran/533945/plateau_tokyo23ku_tran_533945.gml"
                && file.RequiresMeshAreaFilter);
    }

    [Fact]
    public void DiscoverIgnoresFilesOutsideRecognizedUdxPackageLayout()
    {
        using TemporaryDirectory datasetRoot = new();
        Directory.CreateDirectory(Path.Combine(datasetRoot.Path, "misc"));
        File.WriteAllText(Path.Combine(datasetRoot.Path, "misc", "53394525_misc.gml"), "<root />");
        Directory.CreateDirectory(Path.Combine(datasetRoot.Path, "udx", "unknown", "53394525"));
        File.WriteAllText(
            Path.Combine(datasetRoot.Path, "udx", "unknown", "53394525", "plateau_tokyo23ku_unknown_53394525.gml"),
            "<root />");

        IReadOnlyList<LocalCityGmlSourceFileDescriptor> result = LocalCityGmlSourceFileDiscovery.Discover(
            datasetRoot.Path,
            "53394525",
            packageNames: null);

        Assert.Empty(result);
    }
}
