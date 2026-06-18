using PlateauResoniteLink.Application.Importing.CityGml;

using System.IO;
using System.Linq;
using System.Collections.Generic;


namespace PlateauResoniteLink.Tests.Profiles;

public sealed class LocalCityGmlSourceFileDiscoveryTests
{
    private static IEnumerable<string> GetRelativeGmlPaths(string datasetRoot)
    {
        return Directory.EnumerateFiles(datasetRoot, "*.gml", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(datasetRoot, path));
    }

    [Fact]
    public void DiscoverOrdersPackagesFromRequestedCenterOutward()
    {
        LocalCityGmlSourceFileDescriptor[] result = LocalCityGmlSourceFileDiscovery.Discover(
            [
                "udx/tran/53394525/plateau_tokyo23ku_tran_53394525.gml",
                "udx/luse/53394525/plateau_tokyo23ku_luse_53394525.gml",
                "udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml",
                "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
            ],
            "53394525",
            packageNames: null).SourceFiles.ToArray();

        Assert.Equal(["bldg", "dem", "luse", "tran"], result.Select(static file => file.PackageName).ToArray());
        Assert.Equal(
            "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
            result[0].RelativePath);
        Assert.All(result, static file => Assert.False(file.RequiresMeshCodeBoundsFilter));
    }

    [Fact]
    public void DiscoverFiltersPackagesAndKeepsParentMeshMatches()
    {
        string datasetRoot = TestData.GetFixturePath("LocalPlateauDatasetParentMeshPackages");
        IEnumerable<string> relativePaths = GetRelativeGmlPaths(datasetRoot);

        LocalCityGmlSourceFileDiscoveryResult discoveryResult = LocalCityGmlSourceFileDiscovery.Discover(
            relativePaths,
            "53394525",
            ["waterbody", "tran", "dem"]);
        LocalCityGmlSourceFileDescriptor[] result = discoveryResult.SourceFiles.ToArray();

        Assert.Equal(["dem", "tran"], result.Select(static file => file.PackageName).ToArray());
        Assert.Equal(["53394525"], discoveryResult.SelectedMeshCodes);
        Assert.Contains(
            result,
            static file =>
                file.RelativePath == "udx/dem/533945/plateau_tokyo23ku_dem_533945.gml"
                && file.RequiresMeshCodeBoundsFilter);
        Assert.Contains(
            result,
            static file =>
                file.RelativePath == "udx/tran/533945/plateau_tokyo23ku_tran_533945.gml"
                && file.RequiresMeshCodeBoundsFilter);
    }

    [Fact]
    public void DiscoverParentDemRequestIncludesRequestedDetailedDemFiles()
    {
        LocalCityGmlSourceFileDiscoveryResult discoveryResult = LocalCityGmlSourceFileDiscovery.Discover(
            [
                "udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml",
                "udx/dem/533945/plateau_tokyo23ku_dem_53394526.gml",
                "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
            ],
            "533945",
            packageNames: null);
        LocalCityGmlSourceFileDescriptor[] descriptors = discoveryResult.SourceFiles.ToArray();

        Assert.Equal(["53394525", "53394526"], discoveryResult.SelectedMeshCodes);
        Assert.Equal(
            [
                "udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml",
                "udx/dem/533945/plateau_tokyo23ku_dem_53394526.gml",
            ],
            descriptors.Select(static descriptor => descriptor.RelativePath).ToArray());
        Assert.All(descriptors, static descriptor => Assert.Equal("dem", descriptor.PackageName));
        Assert.Equal(["53394525", "53394526"], descriptors.Select(static descriptor => descriptor.MatchedMeshCode).ToArray());
        Assert.All(descriptors, static descriptor => Assert.False(descriptor.RequiresMeshCodeBoundsFilter));
    }

    [Fact]
    public void DiscoverMatchesRegexMeshCodesFromFileNamesAndDirectories()
    {
        LocalCityGmlSourceFileDescriptor[] result = LocalCityGmlSourceFileDiscovery.Discover(
            [
                "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
                "udx/tran/53394526/plateau_tokyo23ku_tran_mesh.gml",
                "udx/dem/53394527/plateau_tokyo23ku_dem_53394527.gml",
                "udx/bldg/533945/plateau_tokyo23ku_bldg_533945.gml",
            ],
            "5339452[56]",
            packageNames: null).SourceFiles.ToArray();

        Assert.Equal(
            [
                "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
                "udx/tran/53394526/plateau_tokyo23ku_tran_mesh.gml",
                "udx/bldg/533945/plateau_tokyo23ku_bldg_533945.gml",
            ],
            result.Select(static file => file.RelativePath).ToArray());
        Assert.Equal(["53394525", "53394526", "533945"], result.Select(static file => file.MatchedMeshCode).ToArray());
        Assert.Equal([false, false, true], result.Select(static file => file.RequiresMeshCodeBoundsFilter).ToArray());
    }

    [Fact]
    public void DiscoverRegexSelectionKeepsParentMeshFilesForMatchedDetailedMeshes()
    {
        string datasetRoot = TestData.GetFixturePath("LocalPlateauDatasetParentMeshPackages");
        IEnumerable<string> relativePaths = GetRelativeGmlPaths(datasetRoot);

        LocalCityGmlSourceFileDiscoveryResult discoveryResult = LocalCityGmlSourceFileDiscovery.Discover(
            relativePaths,
            "5339452.",
            ["dem", "tran"]);
        LocalCityGmlSourceFileDescriptor[] result = discoveryResult.SourceFiles.ToArray();

        Assert.Equal(["dem", "tran"], result.Select(static file => file.PackageName).ToArray());
        Assert.All(result, static file => Assert.Equal("533945", file.MatchedMeshCode));
        Assert.All(result, static file => Assert.True(file.RequiresMeshCodeBoundsFilter));
        Assert.Contains("53394525", discoveryResult.SelectedMeshCodes);
    }

    [Fact]
    public void DiscoverRegexSelectionDerivesSelectedMeshCodesOnlyFromRequestedPackages()
    {
        LocalCityGmlSourceFileDiscoveryResult result = LocalCityGmlSourceFileDiscovery.Discover(
            [
                "udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml",
                "udx/bldg/53394526/plateau_tokyo23ku_bldg_53394526.gml",
            ],
            "5339452.",
            ["dem"]);

        Assert.Equal(["53394525"], result.SelectedMeshCodes);
        Assert.Equal(
            ["udx/dem/53394525/plateau_tokyo23ku_dem_53394525.gml"],
            result.SourceFiles.Select(static file => file.RelativePath).ToArray());
    }

    [Fact]
    public void DiscoverOrdersSamePriorityPackagesFromRequestedCenterOutward()
    {
        LocalCityGmlSourceFileDescriptor[] result = LocalCityGmlSourceFileDiscovery.Discover(
            [
                "udx/bldg/53394521/plateau_tokyo23ku_bldg_53394521.gml",
                "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
                "udx/bldg/53394529/plateau_tokyo23ku_bldg_53394529.gml",
            ],
            "5339452[159]",
            ["bldg"]).SourceFiles.ToArray();

        Assert.Equal(
            [
                "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
                "udx/bldg/53394521/plateau_tokyo23ku_bldg_53394521.gml",
                "udx/bldg/53394529/plateau_tokyo23ku_bldg_53394529.gml",
            ],
            result.Select(static file => file.RelativePath).ToArray());
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
        IEnumerable<string> relativePaths = GetRelativeGmlPaths(datasetRoot.Path);

        LocalCityGmlSourceFileDescriptor[] result = LocalCityGmlSourceFileDiscovery.Discover(
            relativePaths,
            "53394525",
            packageNames: null).SourceFiles.ToArray();

        Assert.Empty(result);
    }
}
