using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class CliArgumentsParserTests
{
    [Fact]
    public void ParseParsesLocalBuildCommand()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--citygml-source",
                "/data/plateau",
                "--resonitelink-port",
                "12345",
            ]);

        Assert.Null(result.Error);
        Assert.Equal("tokyo23ku", result.Options!.Request.Dataset);
        Assert.Equal(DatasetSourceKind.Local, result.Options.Request.SourceKind);
        Assert.Equal("/data/plateau", result.Options.Request.LocalSourcePath);
        Assert.Null(result.Options.Request.DemTextureSource);
        Assert.Equal(new Uri("ws://localhost:12345/"), result.Options.ResoniteLinkUri);
    }

    [Fact]
    public void ParseParsesRemoteBuildCommandAndOptionalOrthoSource()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--citygml-source",
                "https://example.invalid/plateau.zip",
                "--ortho-source",
                "https://example.invalid/53394525.tif",
                "--resonitelink-url",
                "ws://localhost:12345/",
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
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--resonitelink-port",
                "12345",
            ]);

        Assert.Equal("Specify --citygml-source.", result.Error);
    }

    [Fact]
    public void ParseParsesSearchCommand()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "search",
                "--local-source-path",
                "/data/plateau.zip",
                "--mesh-code",
                "5339452[56]",
                "--packages",
                "bldg,tran",
                "--format",
                "json",
            ]);

        Assert.Null(result.Error);
        SearchCommandOptions command = Assert.IsType<SearchCommandOptions>(result.Command);
        Assert.Equal("/data/plateau.zip", command.LocalSourcePath);
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
                "--local-source-path",
                "/data/plateau",
                "--packages",
                "dem,bldg",
            ]);

        Assert.Null(result.Error);
        StatsCommandOptions command = Assert.IsType<StatsCommandOptions>(result.Command);
        Assert.Equal("/data/plateau", command.LocalSourcePath);
        Assert.Equal(["dem", "bldg"], command.PackageNames);
    }

    [Fact]
    public void HelpTextDocumentsUnifiedSourceOptions()
    {
        Assert.Contains("--citygml-source <path-or-url>", CliArgumentsParser.HelpText, StringComparison.Ordinal);
        Assert.Contains("--ortho-source <path-or-url>", CliArgumentsParser.HelpText, StringComparison.Ordinal);
        Assert.DoesNotContain("--source <value>", CliArgumentsParser.HelpText, StringComparison.Ordinal);
        Assert.DoesNotContain("--server-url <url>", CliArgumentsParser.HelpText, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsDeprecatedTileOption()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset",
                "tokyo23ku",
                "--tile",
                "53394525",
            ]);

        Assert.Equal("The --tile option has been replaced. Use --mesh-code.", result.Error);
    }
}
