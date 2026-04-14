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
    public async Task CreateWorkerClientAsyncRequiresWorkerTrackingAndConnectsLazilyOnFirstUse()
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
        Assert.Single(clientFactory.CreatedClients);
        Assert.Equal(1, setupClient.ConnectCallCount);

        string createdSlotId = await workerClient.AddSlotAsync(
            CreateSlotRequest(),
            CancellationToken.None);

        RecordingResoniteLinkClient recordedWorkerClient = clientFactory.CreatedClients[1];
        Assert.Equal("srv_slot_1", createdSlotId);
        Assert.NotSame(setupClient, recordedWorkerClient);
        Assert.Equal(2, clientFactory.CreatedClients.Count);
        Assert.Equal(1, recordedWorkerClient.ConnectCallCount);
        Assert.Equal(1, recordedWorkerClient.AddSlotCallCount);
        Assert.Same(setupClient, session.SetupClient);
        Assert.False(setupClient.Disposed);
        Assert.False(recordedWorkerClient.Disposed);
    }

    [Fact]
    public async Task CreateLaneClientAsyncUsesSetupClientForLaneZeroAndCachesWorkerLaneClient()
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
        IResoniteLinkClient laneOneClientAgain = await session.CreateLaneClientAsync(
            request,
            laneIndex: 1,
            CancellationToken.None);

        RecordingResoniteLinkClient setupClient = clientFactory.CreatedClients[0];
        Assert.Same(setupClient, laneZeroClient);
        Assert.Same(setupClient, session.SetupClient);
        Assert.Same(laneOneClient, laneOneClientAgain);
        Assert.Single(clientFactory.CreatedClients);

        await laneOneClient.AddSlotAsync(CreateSlotRequest(), CancellationToken.None);

        RecordingResoniteLinkClient workerClient = clientFactory.CreatedClients[1];
        Assert.NotSame(setupClient, workerClient);
        Assert.Equal(2, clientFactory.CreatedClients.Count);
        Assert.Equal(1, workerClient.ConnectCallCount);
        Assert.False(setupClient.Disposed);
        Assert.False(workerClient.Disposed);
    }

    [Fact]
    public async Task CreateWorkerClientAsyncRetriesFailedLazyConnectWithoutTouchingSetupClient()
    {
        RecordingClientFactory clientFactory = new([true, false, true]);
        using LiveSendClientSession session = new(
            clientFactory.Create,
            new Uri("ws://localhost:12345/"),
            connectionCount: 2,
            workerConnectTimeoutMilliseconds: 1000,
            progressReporter: null);

        PlateauImportRequest request = CreateRequest();

        await session.EnsureSetupClientConnectedAsync(request, CancellationToken.None);
        session.BeginWorkerClientTracking();

        IResoniteLinkClient workerClient = await session.CreateWorkerClientAsync(
            request,
            laneIndex: 1,
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workerClient.AddSlotAsync(CreateSlotRequest(), CancellationToken.None));

        RecordingResoniteLinkClient setupClient = clientFactory.CreatedClients[0];
        RecordingResoniteLinkClient failedWorkerClient = clientFactory.CreatedClients[1];
        Assert.False(setupClient.Disposed);
        Assert.Same(setupClient, session.SetupClient);
        Assert.True(failedWorkerClient.Disposed);

        string createdSlotId = await workerClient.AddSlotAsync(CreateSlotRequest(), CancellationToken.None);

        RecordingResoniteLinkClient recoveredWorkerClient = clientFactory.CreatedClients[2];
        Assert.Equal("srv_slot_1", createdSlotId);
        Assert.Equal(3, clientFactory.CreatedClients.Count);
        Assert.Equal(1, recoveredWorkerClient.ConnectCallCount);
        Assert.Equal(1, recoveredWorkerClient.AddSlotCallCount);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public async Task CreateLaneClientAsyncRejectsLaneOutsideConfiguredCapacity(int laneIndex)
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
        session.BeginWorkerClientTracking();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => session.CreateLaneClientAsync(request, laneIndex, CancellationToken.None));
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

    private static AddSlot CreateSlotRequest()
    {
        return new AddSlot
        {
            Data = new Slot
            {
                ID = null!,
                Parent = new Reference
                {
                    TargetID = "parent-id",
                },
                Name = new Field_string
                {
                    Value = "Slot",
                },
            },
        };
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
        private int nextSlotId;

        public int ConnectCallCount { get; private set; }

        public int AddSlotCallCount { get; private set; }

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
            cancellationToken.ThrowIfCancellationRequested();
            AddSlotCallCount++;
            return Task.FromResult($"srv_slot_{Interlocked.Increment(ref nextSlotId)}");
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
