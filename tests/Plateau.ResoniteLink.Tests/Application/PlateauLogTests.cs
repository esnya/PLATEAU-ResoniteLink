using Plateau.ResoniteLink.Application.Logging;

namespace Plateau.ResoniteLink.Tests.Application;

public sealed class PlateauLogTests
{
    [Fact]
    public void TryParseReturnsEntryForStructuredMessage()
    {
        bool parsed = PlateauLogEntry.TryParse("[import][warn] delayed", out PlateauLogEntry entry);

        Assert.True(parsed);
        Assert.Equal("import", entry.Scope);
        Assert.Equal(PlateauLogLevel.Warning, entry.Level);
        Assert.Equal("delayed", entry.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain text")]
    [InlineData("[import] delayed")]
    [InlineData("[import][warn]")]
    [InlineData("[import][WARN] delayed")]
    public void TryParseReturnsFalseForMalformedMessage(string value)
    {
        bool parsed = PlateauLogEntry.TryParse(value, out PlateauLogEntry entry);

        Assert.False(parsed);
        Assert.Equal(default, entry);
    }

    [Fact]
    public void NormalizeLegacyMessageWrapsPlainMessageWithDefaultScope()
    {
        string normalized = PlateauLog.NormalizeLegacyMessage("hello world");

        Assert.Equal("[app][info] hello world", normalized);
    }

    [Fact]
    public void NormalizeLegacyMessagePromotesLegacyScopedMessage()
    {
        string normalized = PlateauLog.NormalizeLegacyMessage("[live] connected", PlateauLogLevel.Warning);

        Assert.Equal("[live][warn] connected", normalized);
    }

    [Fact]
    public void NormalizeLegacyMessageLeavesStructuredMessageUntouched()
    {
        const string message = "[live][error] failed";

        string normalized = PlateauLog.NormalizeLegacyMessage(message);

        Assert.Same(message, normalized);
    }

    [Fact]
    public void NormalizeLegacyMessageRejectsNullInput()
    {
        Assert.Throws<ArgumentNullException>(() => PlateauLog.NormalizeLegacyMessage(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void NormalizeLegacyMessageRejectsBlankInput(string value)
    {
        Assert.Throws<ArgumentException>(() => PlateauLog.NormalizeLegacyMessage(value));
    }
}
