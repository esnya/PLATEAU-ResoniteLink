using PlateauResoniteLink.Application.Logging;

namespace PlateauResoniteLink.Tests.Application;

public sealed class PlateauLogTests
{
    [Theory]
    [InlineData("[live][warn] Send lane 1/1 canceled.", PlateauLogLevel.Warning, "[live][warn] Send lane 1/1 canceled.")]
    [InlineData("[live][error] Send lane 1/1 failed: boom", PlateauLogLevel.Error, "[live][error] Send lane 1/1 failed: boom")]
    [InlineData("[live][debug] Preparing city objects.", PlateauLogLevel.Debug, "[live][debug] Preparing city objects.")]
    [InlineData("[app][info] Plain message", PlateauLogLevel.Info, "[app][info] Plain message")]
    public void EntryParserReadsStructuredLogLevel(string message, PlateauLogLevel expectedLevel, string expectedNormalizedMessage)
    {
        bool parsed = PlateauLogEntry.TryParse(message, out PlateauLogEntry entry);

        Assert.True(parsed);
        Assert.Equal(expectedLevel, entry.Level);
        Assert.Equal(expectedNormalizedMessage, entry.ToString());
    }
}
