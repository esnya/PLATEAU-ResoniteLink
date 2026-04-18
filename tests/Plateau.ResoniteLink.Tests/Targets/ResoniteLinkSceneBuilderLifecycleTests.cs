using System.Diagnostics.CodeAnalysis;

using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Targets;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class ResoniteLinkSceneBuilderLifecycleTests
{
    [Fact]
    public async Task EnsureConnectedAsync_DelegatesRequestsToInjectedSession()
    {
        DelegatingClientSession session = new();
        await using ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            new ResoniteLinkSceneBuilderDependencies(
                session,
                new TerrainTextureAssetGenerator()));

        PlateauImportRequest request = CreateRequest();

        await builder.EnsureConnectedAsync(request);
        await builder.EnsureConnectedAsync(request);

        Assert.Equal(2, session.EnsureConnectedCallCount);
        Assert.Equal([request, request], session.EnsureConnectedRequests);
    }

    [Fact]
    public async Task EnsureConnectedAsync_PropagatesInjectedSessionFailure()
    {
        DelegatingClientSession session = new(
            ensureConnectedAsync: static (_, _) => Task.FromException(new InvalidOperationException("connect failed")));
        await using ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            new ResoniteLinkSceneBuilderDependencies(
                session,
                new TerrainTextureAssetGenerator()));

        PlateauImportRequest request = CreateRequest();

        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.EnsureConnectedAsync(request));
        Assert.Equal(1, session.EnsureConnectedCallCount);
    }

    [Fact]
    public async Task DisposeAsync_DisposesInjectedSession()
    {
        DelegatingClientSession session = new();
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            new ResoniteLinkSceneBuilderDependencies(
                session,
                new TerrainTextureAssetGenerator()));

        try
        {
            await builder.DisposeAsync();
            Assert.Equal(1, session.DisposeClientsCallCount);
        }
        finally
        {
            await builder.DisposeAsync();
        }
    }

    private static PlateauImportRequest CreateRequest()
    {
        return new PlateauImportRequest(
            Dataset: "tokyo23ku",
            MeshCode: "53394525",
            SourceKind: DatasetSourceKind.Local,
            LocalSourcePath: Path.Combine(Path.GetTempPath(), "plateau-live-send-boundary"),
            ServerUri: null);
    }
}
