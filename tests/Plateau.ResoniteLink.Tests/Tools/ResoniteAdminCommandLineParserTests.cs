using ResoniteAdmin;

namespace Plateau.ResoniteLink.Tests.Tools;

public sealed class ResoniteAdminCommandLineParserTests
{
    [Fact]
    public void TryParseCleanupModeAcceptsLegacyArguments()
    {
        bool success = ResoniteAdminCommandLineParser.TryParse(
            ["ws://localhost:17136/", "plateau-20202-matsumoto-shi-2020", "--list-only"],
            out ResoniteAdminCommandLineOptions? options,
            out string? error);

        Assert.True(success);
        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal(ResoniteAdminCommandKind.CleanupDataset, options.Kind);
        Assert.Equal(new Uri("ws://localhost:17136/"), options.Endpoint);
        Assert.Equal("plateau-20202-matsumoto-shi-2020", options.Dataset);
        Assert.True(options.ListOnly);
        Assert.False(options.IncludeComponentData);
        Assert.Equal(1, options.Depth);
    }

    [Fact]
    public void TryParseDumpRootModeUsesRecursiveComponentDumpByDefault()
    {
        bool success = ResoniteAdminCommandLineParser.TryParse(
            ["--dump-root", "ws://localhost:17136/"],
            out ResoniteAdminCommandLineOptions? options,
            out string? error);

        Assert.True(success);
        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal(ResoniteAdminCommandKind.DumpRoot, options.Kind);
        Assert.Equal(new Uri("ws://localhost:17136/"), options.Endpoint);
        Assert.Null(options.Dataset);
        Assert.Equal(-1, options.Depth);
        Assert.True(options.IncludeComponentData);
    }

    [Fact]
    public void TryParseDumpRootModeParsesOutputDepthAndExcludeComponentData()
    {
        bool success = ResoniteAdminCommandLineParser.TryParse(
            ["--dump-root", "ws://localhost:17136/", "--output", @"C:\temp\root.json", "--depth", "2", "--exclude-component-data"],
            out ResoniteAdminCommandLineOptions? options,
            out string? error);

        Assert.True(success);
        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal(@"C:\temp\root.json", options.OutputPath);
        Assert.Equal(2, options.Depth);
        Assert.False(options.IncludeComponentData);
    }

    [Fact]
    public void TryParseRejectsUnknownCleanupOption()
    {
        bool success = ResoniteAdminCommandLineParser.TryParse(
            ["ws://localhost:17136/", "plateau-20202-matsumoto-shi-2020", "--bogus"],
            out ResoniteAdminCommandLineOptions? options,
            out string? error);

        Assert.False(success);
        Assert.Null(options);
        Assert.Equal("Unknown cleanup option '--bogus'.", error);
    }
}
