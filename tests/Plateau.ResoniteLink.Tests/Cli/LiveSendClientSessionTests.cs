using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class LiveSendClientSessionTests
{
    [Fact]
    public async Task EnsureSetupClientConnectedAsyncUsesSingleSetupClientAcrossRepeatedCalls()
    {
        RecordingClientFactory clientFactory = new([true]);
        using LiveSendClientSession session = new(
            clientFactory.Create,
            new Uri("ws://localhost:12345/"),
            connectionCount: 2,
            workerConnectTimeoutMilliseconds: 1000,
            progressReporter: null);

        PlateauImportRequest request = CreateRequest();

        await session.EnsureSetupClientConnectedAsync(request, CancellationToken.None);
        await session.EnsureSetupClientConnectedAsync(request, CancellationToken.None);

        RecordingResoniteLinkClient client = Assert.Single(clientFactory.CreatedClients);
        Assert.Same(client, session.SetupClient);
        Assert.Equal(1, client.ConnectCallCount);
        Assert.Equal(new Uri("ws://localhost:12345/"), client.LastConnectedEndpoint);
        Assert.False(client.Disposed);
    }

    [Fact]
    public async Task CreateWorkerClientAsyncRequiresWorkerTrackingAfterSetup()
    {
        RecordingClientFactory clientFactory = new([true, true]);
        using LiveSendClientSession session = new(
            clientFactory.Create,
            new Uri("ws://localhost:12345/"),
            connectionCount: 2,
            workerConnectTimeoutMilliseconds: 1000,
            progressReporter: null);

        PlateauImportRequest request = CreateRequest();

        await session.EnsureSetupClientConnectedAsync(request, CancellationToken.None);

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => session.CreateWorkerClientAsync(request, laneIndex: 1, CancellationToken.None));

        session.BeginWorkerClientTracking();
        IResoniteLinkClient workerClient = await session.CreateWorkerClientAsync(
            request,
            laneIndex: 1,
            CancellationToken.None);

        RecordingResoniteLinkClient setupClient = clientFactory.CreatedClients[0];
        RecordingResoniteLinkClient recordedWorkerClient = Assert.IsType<RecordingResoniteLinkClient>(workerClient);
        Assert.NotSame(setupClient, recordedWorkerClient);
        Assert.Equal(2, clientFactory.CreatedClients.Count);
        Assert.Equal(1, setupClient.ConnectCallCount);
        Assert.Equal(1, recordedWorkerClient.ConnectCallCount);
        Assert.Same(setupClient, session.SetupClient);
        Assert.False(setupClient.Disposed);
        Assert.False(recordedWorkerClient.Disposed);
    }

    [Fact]
    public async Task CreateLaneClientAsyncUsesSetupClientForLaneZero()
    {
        RecordingClientFactory clientFactory = new([true, true]);
        using LiveSendClientSession session = new(
            clientFactory.Create,
            new Uri("ws://localhost:12345/"),
            connectionCount: 2,
            workerConnectTimeoutMilliseconds: 1000,
            progressReporter: null);

        PlateauImportRequest request = CreateRequest();

        await session.EnsureSetupClientConnectedAsync(request, CancellationToken.None);
        session.BeginWorkerClientTracking();

        IResoniteLinkClient laneZeroClient = await session.CreateLaneClientAsync(
            request,
            laneIndex: 0,
            CancellationToken.None);
        IResoniteLinkClient laneOneClient = await session.CreateLaneClientAsync(
            request,
            laneIndex: 1,
            CancellationToken.None);

        RecordingResoniteLinkClient setupClient = clientFactory.CreatedClients[0];
        RecordingResoniteLinkClient workerClient = Assert.IsType<RecordingResoniteLinkClient>(laneOneClient);
        Assert.Same(setupClient, laneZeroClient);
        Assert.Same(setupClient, session.SetupClient);
        Assert.NotSame(setupClient, workerClient);
        Assert.Equal(2, clientFactory.CreatedClients.Count);
        Assert.False(setupClient.Disposed);
        Assert.False(workerClient.Disposed);
    }

    [Fact]
    public async Task CreateWorkerClientAsyncDisposesFailedWorkerClientWithoutTouchingSetupClient()
    {
        RecordingClientFactory clientFactory = new([true, false]);
        using LiveSendClientSession session = new(
            clientFactory.Create,
            new Uri("ws://localhost:12345/"),
            connectionCount: 2,
            workerConnectTimeoutMilliseconds: 1000,
            progressReporter: null);

        PlateauImportRequest request = CreateRequest();

        await session.EnsureSetupClientConnectedAsync(request, CancellationToken.None);
        session.BeginWorkerClientTracking();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.CreateWorkerClientAsync(request, laneIndex: 1, CancellationToken.None));

        RecordingResoniteLinkClient setupClient = clientFactory.CreatedClients[0];
        Assert.False(setupClient.Disposed);
        Assert.Same(setupClient, session.SetupClient);
        Assert.Equal(2, clientFactory.CreatedClients.Count);
        Assert.True(clientFactory.CreatedClients[1].Disposed);
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

    private sealed class RecordingClientFactory(IReadOnlyList<bool> connectOutcomes)
    {
        private int nextConnectOutcomeIndex;

        public List<RecordingResoniteLinkClient> CreatedClients { get; } = [];

        public RecordingResoniteLinkClient Create()
        {
            bool connectSucceeds = nextConnectOutcomeIndex < connectOutcomes.Count
                ? connectOutcomes[nextConnectOutcomeIndex]
                : true;
            nextConnectOutcomeIndex++;

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
