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

        ImportCommandOptions options = AssertImportSuccess(result);
        Assert.Equal("tokyo23ku", options.Request.Dataset);
        Assert.Equal("53394525", options.Request.MeshCode);
        Assert.Equal(DatasetSourceKind.Local, options.Request.CityGmlSourceKind);
        Assert.Equal("/data/plateau", options.Request.CityGmlLocalSourcePath);
        Assert.Null(options.Request.DemTextureSource);
        Assert.Equal(CliTestData.DocumentedDefaultPackageNames, options.Request.PackageNames);
        Assert.Equal(new Uri("ws://localhost:12345/"), options.ResoniteLinkUri);
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

        Assert.Equal("Unknown command 'bogus'.", AssertFailure(result).Error);
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

        ImportCommandOptions options = AssertImportSuccess(result);
        Assert.Equal(DatasetSourceKind.Remote, options.Request.CityGmlSourceKind);
        Assert.Equal(new Uri("https://example.invalid/plateau.zip"), options.Request.CityGmlServerUri);
        Assert.Equal(DatasetSourceKind.Remote, options.Request.DemTextureSourceKind);
        Assert.Equal(new Uri("https://example.invalid/53394525.tif"), options.Request.DemTextureServerUri);
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

        Assert.Equal("Specify --citygml-source.", AssertFailure(result).Error);
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

        ImportCommandOptions options = AssertImportSuccess(result);
        Assert.Null(options.ResoniteLinkUri);
        Assert.Equal("out/scene.json", options.CanonicalSceneDumpPath);
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
            AssertFailure(result).Error);
    }

    [Fact]
    public void ParseRejectsWhitespaceCanonicalSceneDumpPath()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "import",
                "--dataset", "tokyo23ku",
                "--mesh-code", "53394525",
                "--citygml-source", "/data/plateau",
                "--canonical-scene-dump", " ",
            ]);

        Assert.Equal("Specify a non-empty --canonical-scene-dump path.", AssertFailure(result).Error);
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

        ImportCommandOptions options = AssertImportSuccess(result);
        Assert.Equal(["tran", "waterbody", "tran", "brid"], options.Request.PackageNames);
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

        ImportCommandOptions options = AssertImportSuccess(result);
        Assert.Equal("5339452[56]", options.Request.MeshCode);
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

        Assert.Equal("Unknown option '--unexpected'.", AssertFailure(result).Error);
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

        SearchCommandOptions command = AssertSuccess<SearchCommandOptions>(result);
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

        StatsCommandOptions command = AssertSuccess<StatsCommandOptions>(result);
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

        ImportCommandOptions options = AssertImportSuccess(result);
        Assert.Equal(TerrainMeshMode.Grid, options.Request.TerrainMeshMode);
        Assert.Equal(4.5, options.Request.TerrainGridMetersPerVertex, 6);
        Assert.Equal(512, options.Request.TerrainGridMaxResolution);
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

        ImportCommandOptions options = AssertImportSuccess(result);
        Assert.Equal(TerrainMeshMode.Dynamic, options.Request.TerrainMeshMode);
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

    private static ImportCommandOptions AssertImportSuccess(CliParseResult result)
    {
        return Assert.IsType<CliParseResult.ImportSuccessResult>(result).Command;
    }

    private static TCommand AssertSuccess<TCommand>(CliParseResult result)
        where TCommand : CliCommandOptions
    {
        CliCommandOptions command = result switch
        {
            CliParseResult.ImportSuccessResult import => import.Command,
            CliParseResult.SearchSuccessResult search => search.Command,
            CliParseResult.StatsSuccessResult stats => stats.Command,
            _ => throw new InvalidOperationException("Expected successful CLI parse result."),
        };
        return Assert.IsType<TCommand>(command);
    }

    private static CliParseResult.FailureResult AssertFailure(CliParseResult result)
    {
        return Assert.IsType<CliParseResult.FailureResult>(result);
    }
}
