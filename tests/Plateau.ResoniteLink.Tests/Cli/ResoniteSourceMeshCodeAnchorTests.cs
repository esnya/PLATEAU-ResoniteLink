using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class ResoniteSourceMeshCodeAnchorTests
{
    [Fact]
    public void ResolveCompletionMeshCodeUsesNonDemSourceFilenames()
    {
        ResoniteConstructionMetadata metadata = CreateMetadata(
            [
                "udx/dem/533945/plateau_tokyo23ku_dem_533945.gml",
                "udx/bldg/53394526/plateau_tokyo23ku_bldg_53394526.gml",
                "udx/bldg/53394525/plateau_tokyo23ku_bldg_53394525.gml",
            ]);

        string meshCode = ResoniteSourceMeshCodeAnchor.ResolveCompletionMeshCode(metadata);

        Assert.Equal("53394525", meshCode);
    }

    [Fact]
    public void ResolveCompletionMeshCodeFallsBackToDemFilenamesWhenNeeded()
    {
        ResoniteConstructionMetadata metadata = CreateMetadata(
            ["udx/dem/533945/plateau_tokyo23ku_dem_533945.gml"]);

        string meshCode = ResoniteSourceMeshCodeAnchor.ResolveCompletionMeshCode(metadata);

        Assert.Equal("533945", meshCode);
    }

    [Fact]
    public void ResolveCompletionMeshCodeThrowsWhenSourceFilenamesDoNotContainAMeshCode()
    {
        ResoniteConstructionMetadata metadata = CreateMetadata(
            ["udx/bldg/unknown/plateau_tokyo23ku_bldg_regex.gml"]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ResoniteSourceMeshCodeAnchor.ResolveCompletionMeshCode(metadata));

        Assert.Contains("discovered source filenames", exception.Message, StringComparison.Ordinal);
    }

    private static ResoniteConstructionMetadata CreateMetadata(IReadOnlyList<string> sourceFiles)
    {
        return new ResoniteConstructionMetadata(
            SchemaVersion: "3.0",
            WorldName: "PLATEAU tokyo23ku 5339452[56]",
            Request: new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "5339452[56]",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: "/tmp",
                ServerUri: null),
            SourceDataset: new PlateauSourceDataset(
                PackageNames: ["bldg", "dem"],
                SourceFiles: sourceFiles,
                TerrainTextureOverlays: [],
                RequestedMeshCodes: ["53394525", "53394526"]),
            Attribution: new ResoniteAttribution(
                new ResoniteLicenseComponentMetadata(true, "credit", "name", "url"),
                []),
            LocalOrigin: new ResoniteLocalOrigin(35.0, 139.0, 0.0));
    }
}
