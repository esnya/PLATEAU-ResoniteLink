using System;
using System.Collections.Generic;

using PlateauResoniteLink.Targets.Resonite;

namespace PlateauResoniteLink.Tests.Targets;

public sealed class ResoniteSourceMeshCodeAnchorTests
{
    [Fact]
    public void ResolveCompletionMeshCodeUsesNonDemSourceFilenames()
    {
        ResoniteSceneSetupInfo setupInfo = CreateSetupInfo(
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
        ResoniteSceneSetupInfo setupInfo = CreateSetupInfo(
            ["udx/dem/533945/plateau_tokyo23ku_dem_533945.gml"]);

        string meshCode = ResoniteSourceMeshCodeAnchor.ResolveCompletionMeshCode(setupInfo);

        Assert.Equal("533945", meshCode);
    }

    [Fact]
    public void ResolveCompletionMeshCodeThrowsWhenSourceFilenamesDoNotContainAMeshCode()
    {
        ResoniteSceneSetupInfo setupInfo = CreateSetupInfo(
            ["udx/bldg/unknown/plateau_tokyo23ku_bldg_regex.gml"],
            []);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ResoniteSourceMeshCodeAnchor.ResolveCompletionMeshCode(setupInfo));

        Assert.Contains("discovered source filenames", exception.Message, StringComparison.Ordinal);
    }

    private static ResoniteSceneSetupInfo CreateSetupInfo(
        IReadOnlyList<string> sourceFiles,
        IReadOnlyList<string>? selectedMeshCodes = null)
    {
        return new ResoniteSceneSetupInfo(
            Dataset: "tokyo23ku",
            MeshCode: "5339452[56]",
            SourceFiles: sourceFiles,
            SelectedMeshCodes: selectedMeshCodes ?? ["53394525", "53394526"],
            SourceFilePackageNamesByRelativePath: PlateauSourceFilePackageIndex.CreateByRelativePath(sourceFiles),
            DatasetLicense: new ResoniteLicenseAttributionMetadata(true, "credit", "name", "url"));
    }
}
