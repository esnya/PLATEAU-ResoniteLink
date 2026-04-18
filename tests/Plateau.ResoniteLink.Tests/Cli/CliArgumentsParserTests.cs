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
                "--source",
                "local",
                "--local-source-path",
                "/data/plateau",
                "--resonitelink-port",
                "12345",
            ]);

        Assert.Null(result.Error);
        Assert.False(result.ShowHelp);
        Assert.NotNull(result.Options);
        Assert.Equal("tokyo23ku", result.Options.Request.Dataset);
        Assert.Equal("53394525", result.Options.Request.MeshCode);
        Assert.Equal(DatasetSourceKind.Local, result.Options.Request.SourceKind);
        Assert.Equal(CliTestData.DocumentedDefaultPackageNames, result.Options.Request.PackageNames);
        Assert.Equal("local", result.Options.WorkRoot);
        Assert.Equal(new Uri("ws://localhost:12345/"), result.Options.ResoniteLinkUri);
        Assert.Equal(4, result.Options.ResoniteLinkConnectionCount);
        Assert.True(result.Options.EnableMeshBake);
        Assert.False(result.Options.EnableSendMetrics);
        Assert.False(result.Options.VerboseLogging);
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
        Assert.Equal(CliOutputFormat.Text, command.OutputFormat);
    }

    [Fact]
    public void ParseRejectsMissingMeshCodeForSearch()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "search",
                "--local-source-path",
                "/data/plateau",
            ]);

        Assert.Equal("Specify --mesh-code.", result.Error);
    }

    [Fact]
    public void ParseParsesRequestedPackages()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--packages",
                " tran,waterbody,tran,brid ",
                "--resonitelink-port",
                "12345",
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
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "5339452[56]",
                "--local-source-path",
                "/data/plateau",
                "--resonitelink-port",
                "12345",
            ]);

        Assert.Null(result.Error);
        Assert.Equal("5339452[56]", result.Options!.Request.MeshCode);
    }

    [Fact]
    public void ParseParsesUnsupportedPackageName()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--packages",
                "bldg,unknown",
                "--resonitelink-port",
                "12345",
            ]);

        Assert.Null(result.Error);
        Assert.Equal(["bldg", "unknown"], result.Options!.Request.PackageNames);
    }

    [Fact]
    public void ParseRejectsUnknownOption()
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
                "--unexpected",
                "value",
            ]);

        Assert.Equal("Unknown option '--unexpected'.", result.Error);
        Assert.Null(result.Options);
    }

    [Fact]
    public void ParseRejectsMissingOptionValueBeforeAnotherOption()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset",
                "--mesh-code",
                "53394525",
                "--resonitelink-port",
                "12345",
            ]);

        Assert.Equal("A value is required after '--dataset'.", result.Error);
    }

    [Fact]
    public void ParseRejectsMissingValueForResoniteLinkConnections()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--local-source-path",
                "/data/plateau",
                "--resonitelink-port",
                "12345",
                "--resonitelink-connections",
                "--send-metrics",
            ]);

        Assert.Equal("A value is required after '--resonitelink-connections'.", result.Error);
    }

    [Fact]
    public void ParseRejectsNegativeResoniteLinkPortValueAsInvalid()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--source",
                "remote",
                "--server-url",
                "https://example.invalid/plateau.zip",
                "--resonitelink-port",
                "-1",
            ]);

        Assert.Equal("The value '-1' is not a valid TCP port.", result.Error);
    }

    [Fact]
    public void ParseParsesRemoteCommand()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--source",
                "remote",
                "--server-url",
                "https://example.invalid/plateau.zip",
                "--resonitelink-url",
                "ws://localhost:12345/",
            ]);

        Assert.Null(result.Error);
        Assert.Equal(DatasetSourceKind.Remote, result.Options!.Request.SourceKind);
        Assert.Equal(
            new Uri("https://example.invalid/plateau.zip"),
            result.Options.Request.ServerUri);
        Assert.Equal(new Uri("ws://localhost:12345/"), result.Options.ResoniteLinkUri);
    }

    [Fact]
    public void ParseEnablesVerboseLoggingWhenRequested()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--local-source-path",
                "/data/plateau",
                "--resonitelink-port",
                "12345",
                "--verbose",
            ]);

        Assert.Null(result.Error);
        Assert.True(result.Options!.VerboseLogging);
    }

    [Fact]
    public void ParseParsesRemoteCommandWhenServerUrlIsNotDirectArchive()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--source",
                "remote",
                "--server-url",
                "https://example.invalid/plateau",
                "--resonitelink-url",
                "ws://localhost:12345/",
            ]);

        Assert.Null(result.Error);
        Assert.Equal(
            new Uri("https://example.invalid/plateau"),
            result.Options!.Request.ServerUri);
    }

    [Fact]
    public void ParseAcceptsPackagePatternOptionForAliasPackageName()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--local-source-path",
                "/data/plateau",
                "--waterbody-pattern",
                "*Water*",
                "--resonitelink-port",
                "12345",
            ]);

        Assert.Null(result.Error);
        Assert.NotNull(result.Options);
        Assert.NotNull(result.Options.Request.PackagePatterns);
        Assert.Equal("*Water*", result.Options.Request.PackagePatterns["waterbody"]);
    }

    [Fact]
    public void ParsePreservesPackageSpecificLodKeysWithoutSemanticNormalization()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--local-source-path",
                "/data/plateau",
                "--exclude-lod-for-package",
                "waterbody:1,unknown:2",
                "--resonitelink-port",
                "12345",
            ]);

        Assert.Null(result.Error);
        Assert.NotNull(result.Options!.Request.ExcludeLodLevelsByPackage);
        Assert.Equal(2, result.Options.Request.ExcludeLodLevelsByPackage.Count);
        Assert.True(result.Options.Request.ExcludeLodLevelsByPackage.TryGetValue("waterbody", out IReadOnlySet<int>? waterbodyLods));
        Assert.True(result.Options.Request.ExcludeLodLevelsByPackage.TryGetValue("unknown", out IReadOnlySet<int>? unknownLods));
        Assert.Equal(new HashSet<int> { 1 }, waterbodyLods);
        Assert.Equal(new HashSet<int> { 2 }, unknownLods);
    }

    [Fact]
    public void ParseParsesResoniteLinkPort()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--source",
                "local",
                "--local-source-path",
                "/data/plateau",
                "--resonitelink-port",
                "12345",
            ]);

        Assert.Null(result.Error);
        Assert.Equal(new Uri("ws://localhost:12345/"), result.Options!.ResoniteLinkUri);
    }

    [Fact]
    public void ParseParsesResoniteLinkConnectionCount()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--local-source-path",
                "/data/plateau",
                "--resonitelink-port",
                "12345",
                "--resonitelink-connections",
                "8",
            ]);

        Assert.Null(result.Error);
        Assert.Equal(8, result.Options!.ResoniteLinkConnectionCount);
    }

    [Fact]
    public void ParseRejectsInvalidResoniteLinkConnectionCount()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--local-source-path",
                "/data/plateau",
                "--resonitelink-port",
                "12345",
                "--resonitelink-connections",
                "0",
            ]);

        Assert.Equal("The value '0' is not a valid ResoniteLink connection count.", result.Error);
    }

    [Fact]
    public void HelpTextDocumentsParallelResoniteLinkConnections()
    {
        Assert.Contains(
            "Parallel ResoniteLink connection count for live sends. Default: 4.",
            CliArgumentsParser.HelpText,
            StringComparison.Ordinal);
    }

    [Fact]
    public void HelpTextDocumentsInspectionCommands()
    {
        Assert.Contains(
            "plateau-resonitelink search --local-source-path <path> --mesh-code <mesh-code> [options]",
            CliArgumentsParser.HelpText,
            StringComparison.Ordinal);
        Assert.Contains(
            "--format <text|json>   Optional. Output format. Default: text.",
            CliArgumentsParser.HelpText,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsNumericDemTerrainMode()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--local-source-path",
                "/data/plateau",
                "--resonitelink-port",
                "12345",
                "--dem-terrain-mode",
                "2",
            ]);

        Assert.Equal("Unsupported DEM terrain mode '2'. Use 'mesh' or 'heightmap'.", result.Error);
        Assert.Null(result.Options);
    }

    [Fact]
    public void ParseEnablesSendMetrics()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--local-source-path",
                "/data/plateau",
                "--resonitelink-port",
                "12345",
                "--send-metrics",
            ]);

        Assert.Null(result.Error);
        Assert.True(result.Options!.EnableSendMetrics);
    }

    [Fact]
    public void ParseDisablesMeshBakeWhenRequested()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--local-source-path",
                "/data/plateau",
                "--resonitelink-port",
                "12345",
                "--no-mesh-bake",
            ]);

        Assert.Null(result.Error);
        Assert.False(result.Options!.EnableMeshBake);
    }

    [Fact]
    public void ParseParsesDemHeightmapOptions()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--local-source-path",
                "/data/plateau",
                "--resonitelink-port",
                "12345",
                "--dem-terrain-mode",
                "heightmap",
                "--dem-heightmap-meters-per-vertex",
                "4.5",
                "--dem-heightmap-max-resolution",
                "512",
            ]);

        Assert.Null(result.Error);
        Assert.Equal(DemTerrainMode.HeightMap, result.Options!.Request.DemTerrainMode);
        Assert.Equal(4.5, result.Options.Request.DemHeightmapMetersPerVertex, 6);
        Assert.Equal(512, result.Options.Request.DemHeightmapMaxResolution);
    }

    [Fact]
    public void ParseRejectsResoniteLinkPortAndUrlTogether()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--source",
                "local",
                "--local-source-path",
                "/data/plateau",
                "--resonitelink-port",
                "12345",
                "--resonitelink-url",
                "ws://localhost:12346/",
            ]);

        Assert.Equal(
            "Specify either --resonitelink-port or --resonitelink-url, not both.",
            result.Error);
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

    [Fact]
    public void ParseRejectsMissingResoniteLinkEndpoint()
    {
        CliParseResult result = CliArgumentsParser.Parse(
            [
                "build",
                "--dataset",
                "tokyo23ku",
                "--mesh-code",
                "53394525",
                "--source",
                "local",
                "--local-source-path",
                "/data/plateau",
            ]);

        Assert.Equal("Specify either --resonitelink-port or --resonitelink-url.", result.Error);
    }
}
