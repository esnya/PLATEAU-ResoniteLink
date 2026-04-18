using ResoniteSessionTool;

using System.Text.Json;

namespace Plateau.ResoniteLink.Tests.Tools;

public sealed class ResoniteSessionToolCommandLineParserTests
{
    [Fact]
    public void TryParseDiscoverSessionModeUsesUdpDefaults()
    {
        bool success = ResoniteSessionToolCommandLineParser.TryParse(
            ["--discover-session"],
            out ResoniteSessionToolCommandLineOptions? options,
            out string? error);

        Assert.True(success);
        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal(ResoniteSessionToolCommandKind.DiscoverSession, options.Kind);
        Assert.Equal(12512, options.ListenPort);
        Assert.Equal(20, options.TimeoutSeconds);
        Assert.Equal(5, options.MaxAnnouncements);
    }

    [Fact]
    public void TryParseDumpRootModeAcceptsRepoStateAndLabelConvenience()
    {
        bool success = ResoniteSessionToolCommandLineParser.TryParse(
            ["--dump-root", "--repo-path", @"C:\repo", "--state-path", @"C:\repo\runtime\windows\headless\active-session.json", "--label", "baseline", "--depth", "2", "--exclude-component-data"],
            out ResoniteSessionToolCommandLineOptions? options,
            out string? error);

        Assert.True(success);
        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal(ResoniteSessionToolCommandKind.DumpRoot, options.Kind);
        Assert.Null(options.Endpoint);
        Assert.Equal(@"C:\repo", options.RepoPath);
        Assert.Equal(@"C:\repo\runtime\windows\headless\active-session.json", options.StatePath);
        Assert.Equal("baseline", options.Label);
        Assert.Equal(2, options.Depth);
        Assert.False(options.IncludeComponentData);
    }

    [Fact]
    public void TryParseRejectsDumpRootWithoutEndpointOrStateContext()
    {
        bool success = ResoniteSessionToolCommandLineParser.TryParse(
            ["--dump-root", "--depth", "1"],
            out ResoniteSessionToolCommandLineOptions? options,
            out string? error);

        Assert.False(success);
        Assert.Null(options);
        Assert.Equal("Dump-root mode requires <endpoint> or --repo-path/--state-path.", error);
    }

    [Fact]
    public void TryParseCleanupDatasetRootAcceptsVerificationOptions()
    {
        bool success = ResoniteSessionToolCommandLineParser.TryParse(
            ["--cleanup-dataset-root", "ws://localhost:17136/", "plateau-20202-matsumoto-shi-2020", "--repo-path", @"C:\repo", "--list-only", "--verification-timeout-seconds", "30", "--poll-interval-seconds", "3"],
            out ResoniteSessionToolCommandLineOptions? options,
            out string? error);

        Assert.True(success);
        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal(ResoniteSessionToolCommandKind.CleanupDatasetRoot, options.Kind);
        Assert.Equal(new Uri("ws://localhost:17136/"), options.Endpoint);
        Assert.Equal("plateau-20202-matsumoto-shi-2020", options.Dataset);
        Assert.Equal(@"C:\repo", options.RepoPath);
        Assert.True(options.ListOnly);
        Assert.Equal(30, options.VerificationTimeoutSeconds);
        Assert.Equal(3, options.PollIntervalSeconds);
    }

    [Fact]
    public void TryParseStartHeadlessAcceptsLifecycleOptions()
    {
        bool success = ResoniteSessionToolCommandLineParser.TryParse(
            ["--start-headless", "--repo-path", @"C:\repo", "--headless-path", @"C:\Resonite\Headless", "--resonitelink-port", "19001", "--session-name", "Test Session", "--session-description", "Disposable session", "--log-prefix", "headless-smoke", "--startup-timeout-seconds", "90", "--discovery-timeout-seconds", "5", "--state-path", @"C:\repo\runtime\windows\headless\custom-state.json"],
            out ResoniteSessionToolCommandLineOptions? options,
            out string? error);

        Assert.True(success);
        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal(ResoniteSessionToolCommandKind.StartHeadless, options.Kind);
        Assert.Equal(@"C:\repo", options.RepoPath);
        Assert.Equal(@"C:\Resonite\Headless", options.HeadlessPath);
        Assert.Equal(19001, options.ResoniteLinkPort);
        Assert.Equal("Test Session", options.SessionName);
        Assert.Equal("Disposable session", options.SessionDescription);
        Assert.Equal("headless-smoke", options.LogPrefix);
        Assert.Equal(90, options.StartupTimeoutSeconds);
        Assert.Equal(5, options.DiscoveryTimeoutSeconds);
        Assert.Equal(@"C:\repo\runtime\windows\headless\custom-state.json", options.StatePath);
    }

    [Fact]
    public void TryParseStopHeadlessAcceptsExplicitProcessId()
    {
        bool success = ResoniteSessionToolCommandLineParser.TryParse(
            ["--stop-headless", "--process-id", "4321"],
            out ResoniteSessionToolCommandLineOptions? options,
            out string? error);

        Assert.True(success);
        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal(ResoniteSessionToolCommandKind.StopHeadless, options.Kind);
        Assert.Equal(4321, options.ProcessId);
        Assert.Null(options.RepoPath);
        Assert.Null(options.StatePath);
    }

    [Fact]
    public void TryParseRejectsMissingOptionValue()
    {
        bool success = ResoniteSessionToolCommandLineParser.TryParse(
            ["--start-headless", "--repo-path"],
            out ResoniteSessionToolCommandLineOptions? options,
            out string? error);

        Assert.False(success);
        Assert.Null(options);
        Assert.Equal("--repo-path requires a value.", error);
    }

    [Fact]
    public void TryParseRejectsUnknownStopHeadlessOption()
    {
        bool success = ResoniteSessionToolCommandLineParser.TryParse(
            ["--stop-headless", "--process-id", "42", "--bogus"],
            out ResoniteSessionToolCommandLineOptions? options,
            out string? error);

        Assert.False(success);
        Assert.Null(options);
        Assert.Equal("Unknown stop-headless option '--bogus'.", error);
    }

    [Fact]
    public void RootDumpCleanupTargetsFindsLowercaseDatasetRoot()
    {
        RootDump dump = CreateRootDump(
            """
            {
              "children": [
                {
                  "id": "Reso_A",
                  "name": {
                    "value": "PLATEAU plateau-20202-matsumoto-shi-2020"
                  }
                },
                {
                  "id": "Reso_B",
                  "name": {
                    "value": "Controllers"
                  }
                }
              ]
            }
            """);

        List<SlotSummary> targets = RootDumpCleanupTargets.FindDatasetRootTargets(
            dump,
            "PLATEAU plateau-20202-matsumoto-shi-2020");

        Assert.Single(targets);
        Assert.Equal("Reso_A", targets[0].Id);
        Assert.Equal("PLATEAU plateau-20202-matsumoto-shi-2020", targets[0].Name);
    }

    [Fact]
    public void RootDumpCleanupTargetsDoesNotTreatSharedAssetsOrDatasetAssetsAsDatasetRoot()
    {
        RootDump dump = CreateRootDump(
            """
            {
              "children": [
                {
                  "id": "Reso_A",
                  "name": {
                    "value": "PLATEAU tokyo23ku - Assets"
                  }
                },
                {
                  "id": "Reso_B",
                  "name": {
                    "value": "PLATEAU Shared Assets"
                  }
                },
                {
                  "id": "Reso_C",
                  "name": {
                    "value": "PLATEAU tokyo23ku"
                  }
                }
              ]
            }
            """);

        List<SlotSummary> targets = RootDumpCleanupTargets.FindDatasetRootTargets(
            dump,
            "PLATEAU tokyo23ku");

        Assert.Single(targets);
        Assert.Equal("Reso_C", targets[0].Id);
    }

    private static RootDump CreateRootDump(string rootJson)
    {
        using JsonDocument document = JsonDocument.Parse(rootJson);
        return new RootDump(
            "ws://localhost:17136/",
            DateTimeOffset.UtcNow,
            1,
            IncludeComponentData: false,
            Root: document.RootElement.Clone());
    }
}
