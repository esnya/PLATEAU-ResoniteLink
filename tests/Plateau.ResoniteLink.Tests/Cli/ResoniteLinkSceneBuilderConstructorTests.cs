using Plateau.ResoniteLink.Cli;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class ResoniteLinkSceneBuilderConstructorTests
{
    [Fact]
    public void ConstructorRejectsNonPositiveConnectionCount()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ResoniteLinkSceneBuilder(
                new Uri("ws://localhost:12345/"),
                0,
                ResoniteLinkSendDiagnostics.Disabled));

        Assert.Equal("connectionCount", exception.ParamName);
    }
}
