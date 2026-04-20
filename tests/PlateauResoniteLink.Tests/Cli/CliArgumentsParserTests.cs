using PlateauResoniteLink.Cli;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Cli;

public sealed class CliArgumentsParserTests
{
    [Fact]
    public void ParseParsesLocalBuildCommand()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset", "tokyo23ku",
                "--mesh-code", "53394525",
                "--citygml-source", "/data/plateau",
                "--resonitelink-port", "12345",
            ]);

        Assert.Null(result.Error);
        Assert.False(result.ShowHelp);
        Assert.NotNull(result.Options);
        Assert.Equal("tokyo23ku", result.Options.Request.Dataset);
        Assert.Equal("53394525", result.Options.Request.MeshCode);
        Assert.Equal(DatasetSourceKind.Local, result.Options.Request.SourceKind);
        Assert.Equal("/data/plateau", result.Options.Request.LocalSourcePath);
        Assert.Null(result.Options.Request.DemTextureSource);
        Assert.Equal(CliTestData.DocumentedDefaultPackageNames, result.Options.Request.PackageNames);
        Assert.Equal(new Uri("ws://localhost:12345/"), result.Options.ResoniteLinkUri);
    }

    [Fact]
    public void ParseParsesRemoteBuildCommandAndOptionalGeoTiffSource()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset", "tokyo23ku",
                "--mesh-code", "53394525",
                "--citygml-source", "https://example.invalid/plateau.zip",
                "--geotiff-source", "https://example.invalid/53394525.tif",
                "--resonitelink-url", "ws://localhost:12345/",
            ]);

        Assert.Null(result.Error);
        Assert.Equal(DatasetSourceKind.Remote, result.Options!.Request.SourceKind);
        Assert.Equal(new Uri("https://example.invalid/plateau.zip"), result.Options.Request.ServerUri);
        Assert.Equal(DatasetSourceKind.Remote, result.Options.Request.DemTextureSourceKind);
        Assert.Equal(new Uri("https://example.invalid/53394525.tif"), result.Options.Request.DemTextureServerUri);
    }

    [Fact]
    public void ParseRequiresCityGmlSource()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset", "tokyo23ku",
                "--mesh-code", "53394525",
                "--resonitelink-port", "12345",
            ]);

        Assert.Equal("Specify --citygml-source.", result.Error);
    }

    [Fact]
    public void ParseRejectsDeprecatedSourceFlags()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset", "tokyo23ku",
                "--mesh-code", "53394525",
                "--source", "local",
                "--resonitelink-port", "12345",
            ]);

        Assert.Equal("The --source option has been replaced. Use --citygml-source.", result.Error);
    }

    [Fact]
    public void ParseRejectsDeprecatedServerUrlFlag()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset", "tokyo23ku",
                "--mesh-code", "53394525",
                "--server-url", "https://example.invalid/plateau.zip",
                "--resonitelink-port", "12345",
            ]);

        Assert.Equal("The --server-url option has been replaced. Use --citygml-source.", result.Error);
    }

    [Fact]
    public void ParseParsesRequestedPackages()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset", "tokyo23ku",
                "--mesh-code", "53394525",
                "--citygml-source", "/data/plateau",
                "--packages", " tran,waterbody,tran,brid ",
                "--resonitelink-port", "12345",
            ]);

        Assert.Null(result.Error);
        Assert.Equal(["tran", "waterbody", "tran", "brid"], result.Options!.Request.PackageNames);
    }

    [Fact]
    public void ParsePreservesRegexMeshCode()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset", "tokyo23ku",
                "--mesh-code", "5339452[56]",
                "--citygml-source", "/data/plateau",
                "--resonitelink-port", "12345",
            ]);

        Assert.Null(result.Error);
        Assert.Equal("5339452[56]", result.Options!.Request.MeshCode);
    }

    [Fact]
    public void ParseRejectsUnknownOption()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset", "tokyo23ku",
                "--mesh-code", "53394525",
                "--citygml-source", "/data/plateau",
                "--resonitelink-port", "12345",
                "--unexpected", "value",
            ]);

        Assert.Equal("Unknown option '--unexpected'.", result.Error);
    }

    [Fact]
    public void ParseParsesSearchCommand()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "search",
                "--citygml-source", "/data/plateau.zip",
                "--mesh-code", "5339452[56]",
                "--packages", "bldg,tran",
                "--format", "json",
            ]);

        Assert.Null(result.Error);
        SearchCommandOptions command = Assert.IsType<SearchCommandOptions>(result.Command);
        Assert.Equal("/data/plateau.zip", command.CityGmlSourcePath);
        Assert.Equal("5339452[56]", command.MeshCode);
        Assert.Equal(["bldg", "tran"], command.PackageNames);
        Assert.Equal(CliOutputFormat.Json, command.OutputFormat);
    }

    [Fact]
    public void ParseParsesStatsCommand()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "stats",
                "--citygml-source", "/data/plateau",
                "--packages", "dem,bldg",
            ]);

        Assert.Null(result.Error);
        StatsCommandOptions command = Assert.IsType<StatsCommandOptions>(result.Command);
        Assert.Equal("/data/plateau", command.CityGmlSourcePath);
        Assert.Equal(["dem", "bldg"], command.PackageNames);
    }

    [Fact]
    public void ParseParsesDemHeightmapOptions()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset", "tokyo23ku",
                "--mesh-code", "53394525",
                "--citygml-source", "/data/plateau",
                "--resonitelink-port", "12345",
                "--dem-terrain-mode", "heightmap",
                "--dem-heightmap-meters-per-vertex", "4.5",
                "--dem-heightmap-max-resolution", "512",
            ]);

        Assert.Null(result.Error);
        Assert.Equal(DemTerrainMode.HeightMap, result.Options!.Request.DemTerrainMode);
        Assert.Equal(4.5, result.Options.Request.DemHeightmapMetersPerVertex, 6);
        Assert.Equal(512, result.Options.Request.DemHeightmapMaxResolution);
    }

    [Fact]
    public void HelpTextDocumentsUnifiedSourceOptions()
    {
        Assert.Contains("--citygml-source <path-or-url>", CliArgumentsParser.HelpText, StringComparison.Ordinal);
        Assert.Contains("--geotiff-source <path-or-url>", CliArgumentsParser.HelpText, StringComparison.Ordinal);
        Assert.DoesNotContain("--local-source-path <path>", CliArgumentsParser.HelpText, StringComparison.Ordinal);
        Assert.DoesNotContain("--source <value>", CliArgumentsParser.HelpText, StringComparison.Ordinal);
        Assert.DoesNotContain("--server-url <url>", CliArgumentsParser.HelpText, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsDeprecatedOrthoSourceFlag()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset", "tokyo23ku",
                "--mesh-code", "53394525",
                "--citygml-source", "/data/plateau.zip",
                "--ortho-source", "/data/53394525.tif",
                "--resonitelink-port", "12345",
            ]);

        Assert.Equal("The --ortho-source option has been replaced. Use --geotiff-source.", result.Error);
    }

    [Fact]
    public void ParseRejectsDeprecatedLocalSourcePathForSearch()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "search",
                "--local-source-path", "/data/plateau.zip",
                "--mesh-code", "53394525",
            ]);

        Assert.Equal("The --local-source-path option has been replaced. Use --citygml-source.", result.Error);
    }

    [Fact]
    public void ParseRejectsDeprecatedTileOption()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset", "tokyo23ku",
                "--tile", "53394525",
            ]);

        Assert.Equal("The --tile option has been replaced. Use --mesh-code.", result.Error);
    }
}
