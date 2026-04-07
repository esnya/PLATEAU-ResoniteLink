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
        Assert.Equal(PlateauPackageCatalog.CliDefaultPackageNames, result.Options.Request.PackageNames);
        Assert.Equal(
            Path.Combine("runtime", GetCurrentOsDirectoryName(), "resonite"),
            result.Options.WorkRoot);
        Assert.Equal(new Uri("ws://localhost:12345/"), result.Options.ResoniteLinkUri);
        Assert.Equal(4, result.Options.ResoniteLinkConnectionCount);
        Assert.False(result.Options.EnableSendMetrics);
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
        Assert.Equal(["tran", "wtr", "brid"], result.Options!.Request.PackageNames);
    }

    [Fact]
    public void ParseRejectsUnsupportedPackageName()
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

        Assert.Equal(
            "Unsupported package name(s): unknown. Supported packages: area, bldg, brid, cons, dem, fld, frn, gen, htd, ifld, lsld, luse, rfld, rwy, squr, tnm, tran, trk, tun, ubld, unf, urf, veg, wtr, wwy.",
            result.Error);
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
                "https://example.invalid/plateau",
                "--resonitelink-url",
                "ws://localhost:12345/",
            ]);

        Assert.Null(result.Error);
        Assert.Equal(DatasetSourceKind.Remote, result.Options!.Request.SourceKind);
        Assert.Equal(
            new Uri("https://example.invalid/plateau"),
            result.Options.Request.ServerUri);
        Assert.Equal(new Uri("ws://localhost:12345/"), result.Options.ResoniteLinkUri);
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

    private static string GetCurrentOsDirectoryName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }

        if (OperatingSystem.IsLinux())
        {
            return "linux";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "macos";
        }

        return "unknown";
    }
}
