using System;

using PlateauResoniteLink.Cli;
using PlateauResoniteLink.Domain.Importing;

namespace PlateauResoniteLink.Tests.Cli;

public sealed class CliArgumentsParserTests
{
    [Fact]
    public void ParseParsesLocalImportCommand()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "import",
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
    public void ParseRejectsUnknownCommandToken()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "bogus",
                "--dataset", "tokyo23ku",
                "--mesh-code", "53394525",
                "--citygml-source", "/data/plateau",
                "--resonitelink-port", "12345",
            ]);

        Assert.Equal("Unknown command 'bogus'.", result.Error);
        Assert.False(result.ShowHelp);
        Assert.Null(result.Options);
    }

    [Fact]
    public void ParseParsesRemoteImportCommandAndOptionalGeoTiffSource()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "import",
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
                "import",
                "--dataset", "tokyo23ku",
                "--mesh-code", "53394525",
                "--resonitelink-port", "12345",
            ]);

        Assert.Equal("Specify --citygml-source.", result.Error);
    }

    [Fact]
    public void ParseParsesCanonicalSceneDumpImportWithoutResoniteLinkEndpoint()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "import",
                "--dataset", "tokyo23ku",
                "--mesh-code", "53394525",
                "--citygml-source", "/data/plateau",
                "--canonical-scene-dump", "out/scene.json",
            ]);

        Assert.Null(result.Error);
        Assert.Null(result.Options!.ResoniteLinkUri);
        Assert.Equal("out/scene.json", result.Options.CanonicalSceneDumpPath);
    }

    [Fact]
    public void ParseRejectsCanonicalSceneDumpWithResoniteLinkEndpoint()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "import",
                "--dataset", "tokyo23ku",
                "--mesh-code", "53394525",
                "--citygml-source", "/data/plateau",
                "--canonical-scene-dump", "out/scene.json",
                "--resonitelink-port", "12345",
            ]);

        Assert.Equal(
            "Do not specify --resonitelink-port or --resonitelink-url when --canonical-scene-dump is used.",
            result.Error);
    }

    [Fact]
    public void ParseParsesRequestedPackages()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "import",
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
                "import",
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
                "import",
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
    public void ParseParsesTerrainGridOptions()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "import",
                "--dataset", "tokyo23ku",
                "--mesh-code", "53394525",
                "--citygml-source", "/data/plateau",
                "--resonitelink-port", "12345",
                "--terrain-mesh", "grid",
                "--terrain-grid-meters-per-vertex", "4.5",
                "--terrain-grid-max-resolution", "512",
            ]);

        Assert.Null(result.Error);
        Assert.Equal(TerrainMeshMode.Grid, result.Options!.Request.TerrainMeshMode);
        Assert.Equal(4.5, result.Options.Request.TerrainGridMetersPerVertex, 6);
        Assert.Equal(512, result.Options.Request.TerrainGridMaxResolution);
    }

    [Fact]
    public void ParseParsesTerrainDynamicMode()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "import",
                "--dataset", "tokyo23ku",
                "--mesh-code", "53394525",
                "--citygml-source", "/data/plateau",
                "--resonitelink-port", "12345",
                "--terrain-mesh", "dynamic",
            ]);

        Assert.Null(result.Error);
        Assert.Equal(TerrainMeshMode.Dynamic, result.Options!.Request.TerrainMeshMode);
    }

    [Fact]
    public void HelpTextDocumentsUnifiedSourceOptions()
    {
        Assert.Contains(
            "plateau-resonitelink import --dataset <dataset> --mesh-code <mesh-code> [options]",
            CliArgumentsParser.HelpText,
            StringComparison.Ordinal);
        Assert.Contains(
            "Import options:",
            CliArgumentsParser.HelpText,
            StringComparison.Ordinal);
        Assert.Contains("--citygml-source <path-or-url>", CliArgumentsParser.HelpText, StringComparison.Ordinal);
        Assert.Contains("--geotiff-source <path-or-url>", CliArgumentsParser.HelpText, StringComparison.Ordinal);
    }

}
