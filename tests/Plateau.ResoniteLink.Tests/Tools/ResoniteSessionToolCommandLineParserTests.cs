using ResoniteSessionTool;

namespace Plateau.ResoniteLink.Tests.Tools;

public sealed class ResoniteSessionToolCommandLineParserTests
{
    [Fact]
    public void TryParseDumpRootModeUsesRecursiveComponentDumpByDefault()
    {
        bool success = ResoniteSessionToolCommandLineParser.TryParse(
            ["--dump-root", "ws://localhost:17136/"],
            out ResoniteSessionToolCommandLineOptions? options,
            out string? error);

        Assert.True(success);
        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal(ResoniteSessionToolCommandKind.DumpRoot, options.Kind);
        Assert.Equal(new Uri("ws://localhost:17136/"), options.Endpoint);
        Assert.Null(options.SlotId);
        Assert.Equal(-1, options.Depth);
        Assert.True(options.IncludeComponentData);
    }

    [Fact]
    public void TryParseDumpRootModeParsesOutputDepthAndExcludeComponentData()
    {
        bool success = ResoniteSessionToolCommandLineParser.TryParse(
            ["--dump-root", "ws://localhost:17136/", "--output", @"C:\temp\root.json", "--depth", "2", "--exclude-component-data"],
            out ResoniteSessionToolCommandLineOptions? options,
            out string? error);

        Assert.True(success);
        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal(@"C:\temp\root.json", options.OutputPath);
        Assert.Equal(2, options.Depth);
        Assert.False(options.IncludeComponentData);
    }

    [Fact]
    public void TryParseRemoveSlotModeAcceptsEndpointAndSlotId()
    {
        bool success = ResoniteSessionToolCommandLineParser.TryParse(
            ["--remove-slot", "ws://localhost:17136/", "root-123"],
            out ResoniteSessionToolCommandLineOptions? options,
            out string? error);

        Assert.True(success);
        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal(ResoniteSessionToolCommandKind.RemoveSlot, options.Kind);
        Assert.Equal(new Uri("ws://localhost:17136/"), options.Endpoint);
        Assert.Equal("root-123", options.SlotId);
        Assert.Equal(1, options.Depth);
        Assert.False(options.IncludeComponentData);
    }

    [Fact]
    public void TryParseRejectsUnknownRemoveSlotOption()
    {
        bool success = ResoniteSessionToolCommandLineParser.TryParse(
            ["--remove-slot", "ws://localhost:17136/", "root-123", "--bogus"],
            out ResoniteSessionToolCommandLineOptions? options,
            out string? error);

        Assert.False(success);
        Assert.Null(options);
        Assert.Equal("Unknown remove-slot option '--bogus'.", error);
    }
}
