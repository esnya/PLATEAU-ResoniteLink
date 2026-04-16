using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class ResoniteLinkSceneBuilderLifecycleTests
{
    [Fact]
    public async Task EnsureConnectedAsyncReusesSingleSetupClientAcrossRepeatedCalls()
    {
        RecordingClientFactory clientFactory = new(connectSucceeds: true);
        await using ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            clientFactory.Create);

        PlateauImportRequest request = CreateRequest();

        await builder.EnsureConnectedAsync(request);
        await builder.EnsureConnectedAsync(request);

        RecordingResoniteLinkClient client = Assert.Single(clientFactory.CreatedClients);
        Assert.Equal(1, client.ConnectCallCount);
        Assert.Equal(new Uri("ws://localhost:12345/"), client.LastConnectedEndpoint);
        Assert.False(client.Disposed);
    }

    [Fact]
    public async Task EnsureConnectedAsyncDisposesSetupClientsWhenConnectFails()
    {
        RecordingClientFactory clientFactory = new(connectSucceeds: false);
        await using ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            clientFactory.Create);

        PlateauImportRequest request = CreateRequest();

        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.EnsureConnectedAsync(request));

        Assert.Equal(2, clientFactory.CreatedClients.Count);
        Assert.All(clientFactory.CreatedClients, client => Assert.True(client.Disposed));
    }

    [Fact]
    public async Task DisposeAsyncDisposesConnectedSetupClient()
    {
        RecordingClientFactory clientFactory = new(connectSucceeds: true);
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            clientFactory.Create);

        try
        {
            PlateauImportRequest request = CreateRequest();

            await builder.EnsureConnectedAsync(request);
            RecordingResoniteLinkClient client = Assert.Single(clientFactory.CreatedClients);
            Assert.False(client.Disposed);

            await builder.DisposeAsync();

            Assert.True(client.Disposed);
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

    private sealed class RecordingClientFactory(bool connectSucceeds)
    {
        public List<RecordingResoniteLinkClient> CreatedClients { get; } = [];

        public RecordingResoniteLinkClient Create()
        {
            RecordingResoniteLinkClient client = new(connectSucceeds);
            CreatedClients.Add(client);
            return client;
        }
    }

    private sealed class RecordingResoniteLinkClient(bool connectSucceeds) : IResoniteLinkClient
    {
        public int ConnectCallCount { get; private set; }

        public Uri? LastConnectedEndpoint { get; private set; }

        public bool Disposed { get; private set; }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            ConnectCallCount++;
            LastConnectedEndpoint = endpoint;
            return connectSucceeds
                ? Task.CompletedTask
                : Task.FromException(new InvalidOperationException("connect failed"));
        }

        public void Dispose()
        {
            Disposed = true;
        }

        public Task<string> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<string> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<BatchResponse> RunDataModelOperationBatchAsync(
            IReadOnlyList<DataModelOperation> operations,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
