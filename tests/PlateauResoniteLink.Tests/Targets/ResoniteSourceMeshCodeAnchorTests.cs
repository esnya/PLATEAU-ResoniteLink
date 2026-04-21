using System;
using System.Collections.Generic;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class ResoniteSourceMeshCodeAnchorTests
{
    [Fact]
    public void ResolveCompletionMeshCodeUsesNonDemSourceFilenames()
    {
        SceneBootstrapInfo setupInfo = CreateSetupInfo(
            [
                "udx/dem/533945/plateau_tokyo23ku_dem_533945.gml",
                "udx/bldg/53394526/plateau_tokyo23ku_bldg_53394526.gml",
                "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
            ]);

        string meshCode = ResoniteSourceMeshCodeAnchor.ResolveCompletionMeshCode(setupInfo);

        Assert.Equal("53394525", meshCode);
    }

    [Fact]
    public void ResolveCompletionMeshCodeFallsBackToDemFilenamesWhenNeeded()
    {
        SceneBootstrapInfo setupInfo = CreateSetupInfo(
            ["udx/dem/533945/plateau_tokyo23ku_dem_533945.gml"]);

        string meshCode = ResoniteSourceMeshCodeAnchor.ResolveCompletionMeshCode(setupInfo);

        Assert.Equal("533945", meshCode);
    }

    [Fact]
    public void ResolveCompletionMeshCodeThrowsWhenSourceFilenamesDoNotContainAMeshCode()
    {
        SceneBootstrapInfo setupInfo = CreateSetupInfo(
            ["udx/bldg/unknown/plateau_tokyo23ku_bldg_regex.gml"],
            []);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ResoniteSourceMeshCodeAnchor.ResolveCompletionMeshCode(setupInfo));

        Assert.Contains("discovered source filenames", exception.Message, StringComparison.Ordinal);
    }

    private static SceneBootstrapInfo CreateSetupInfo(
        IReadOnlyList<string> sourceFiles,
        IReadOnlyList<string>? selectedMeshCodes = null)
    {
        return new SceneBootstrapInfo(
            Dataset: "tokyo23ku",
            MeshCode: "5339452[56]",
            LocalSourcePath: "/tmp",
            PackageNames: ["bldg", "dem"],
            SourceFiles: sourceFiles,
            SelectedMeshCodes: selectedMeshCodes ?? ["53394525", "53394526"],
            DatasetLicense: new LicenseAttributionMetadata(true, "credit", "name", "url"),
            AdditionalDatasetLicenses: []);
    }
}
