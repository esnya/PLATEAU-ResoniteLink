using PlateauResoniteLink.Application.Logging;

namespace PlateauResoniteLink.Tests.Application;

public sealed class PlateauLogTests
{
    [Theory]
    [InlineData("[live][warn] Send lane 1/1 canceled.", PlateauLogLevel.Warning, "[live][warn] Send lane 1/1 canceled.")]
    [InlineData("[live][error] Send lane 1/1 failed: boom", PlateauLogLevel.Error, "[live][error] Send lane 1/1 failed: boom")]
    [InlineData("[live] Preparing city objects.", PlateauLogLevel.Debug, "[live][debug] Preparing city objects.")]
    [InlineData("Plain message", PlateauLogLevel.Info, "[app][info] Plain message")]
    public void NormalizeLegacyMessageInfersExpectedLevel(string message, PlateauLogLevel expectedLevel, string expectedNormalizedMessage)
    {
        PlateauLogLevel defaultLevel = PlateauLog.InferLegacyDefaultLevel(message);
        string normalized = PlateauLog.NormalizeLegacyMessage(message, defaultLevel);

        Assert.Equal(expectedLevel, defaultLevel);
        Assert.Equal(expectedNormalizedMessage, normalized);
    }
}
