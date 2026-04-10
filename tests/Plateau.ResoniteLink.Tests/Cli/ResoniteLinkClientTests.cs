using Plateau.ResoniteLink.Cli;

using ResoniteLink;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class ResoniteLinkClientTests
{
    [Fact]
    public void EnsureSuccessThrowsProtocolErrorWhenResponseIsNull()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ResoniteLinkClient.EnsureSuccess(null, "add component '[FrooxEngine]FrooxEngine.MeshRenderer' on 'slot-1'"));

        Assert.Equal(
            "ResoniteLink add component '[FrooxEngine]FrooxEngine.MeshRenderer' on 'slot-1' returned a null response.",
            exception.Message);
    }

    [Fact]
    public void EnsureSuccessThrowsErrorInfoWhenResponseFails()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ResoniteLinkClient.EnsureSuccess(
                new Response
                {
                    Success = false,
                    ErrorInfo = "server said no",
                },
                "add slot 'Assets' on 'Root'"));

        Assert.Equal(
            "ResoniteLink add slot 'Assets' on 'Root' failed: server said no",
            exception.Message);
    }
}
