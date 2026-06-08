using System.IO;

using Microsoft.Extensions.Logging;

using PlateauResoniteLink.Cli;

namespace PlateauResoniteLink.Tests.Cli;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1848:Use the LoggerMessage delegates", Justification = "This test verifies the ILogger extension path exposed to callers.")]
public sealed class CliTextWriterLoggerProviderTests
{
    [Fact]
    public void LoggerFiltersBelowConfiguredLevel()
    {
        using StringWriter writer = new();
        using CliTextWriterLoggerProvider provider = new(writer, LogLevel.Information);
        ILogger logger = provider.CreateLogger("PlateauResoniteLink.Import");

        logger.Log(
            LogLevel.Debug,
            new EventId(1),
            "Debug detail",
            null,
            static (state, exception) => state);
        logger.Log(
            LogLevel.Information,
            new EventId(2),
            "Import milestone",
            null,
            static (state, exception) => state);

        string output = writer.ToString();
        Assert.DoesNotContain("Debug detail", output);
        Assert.Contains("info", output);
        Assert.Contains("PlateauResoniteLink.Import: Import milestone", output);
    }
}
