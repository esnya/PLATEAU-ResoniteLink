using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class LiveSendClientSessionTests
{
    [Fact]
    public async Task EnsureSetupClientConnectedAsyncConnectsAllConfiguredSessionsEagerly()
    {
        RecordingClientFactory clientFactory = new([new(true), new(true), new(true)]);
        using LiveSendClientSession session = new(
            clientFactory.Create,
            new Uri("ws://localhost:12345/"),
            connectionCount: 3,
            workerConnectTimeoutMilliseconds: 1000,
            progressReporter: null);

        PlateauImportRequest request = CreateRequest();

        await session.EnsureSetupClientConnectedAsync(request, CancellationToken.None);

        Assert.Equal(3, clientFactory.CreatedClients.Count);
        Assert.Same(clientFactory.CreatedClients[0], session.SetupClient);
        Assert.All(
            clientFactory.CreatedClients,
            client => Assert.Equal(1, client.ConnectCallCount));

        IResoniteLinkClient laneZeroClient = await session.CreateLaneClientAsync(request, laneIndex: 0, CancellationToken.None);
        IResoniteLinkClient laneOneClient = await session.CreateLaneClientAsync(request, laneIndex: 1, CancellationToken.None);
        IResoniteLinkClient laneTwoClient = await session.CreateLaneClientAsync(request, laneIndex: 2, CancellationToken.None);
        IResoniteLinkClient laneOneClientAgain = await session.CreateLaneClientAsync(request, laneIndex: 1, CancellationToken.None);

        Assert.Same(clientFactory.CreatedClients[0], laneZeroClient);
        Assert.Same(clientFactory.CreatedClients[1], laneOneClient);
        Assert.Same(clientFactory.CreatedClients[2], laneTwoClient);
        Assert.Same(laneOneClient, laneOneClientAgain);
    }

    [Fact]
    public async Task CreateWorkerClientAsyncUsesExistingConnectedWorkers()
    {
        RecordingClientFactory clientFactory = new([new(true), new(true)]);
        using LiveSendClientSession session = new(
            clientFactory.Create,
            new Uri("ws://localhost:12345/"),
            connectionCount: 2,
            workerConnectTimeoutMilliseconds: 1000,
            progressReporter: null);

        PlateauImportRequest request = CreateRequest();

        await session.EnsureSetupClientConnectedAsync(request, CancellationToken.None);

        IResoniteLinkClient workerClient = await session.CreateWorkerClientAsync(
            request,
            laneIndex: 1,
            CancellationToken.None);

        string createdSlotId = await workerClient.AddSlotAsync(
            CreateSlotRequest(),
            CancellationToken.None);

        RecordingResoniteLinkClient worker = clientFactory.CreatedClients[1];
        Assert.Equal("srv_slot_1", createdSlotId);
        Assert.Equal(1, worker.AddSlotCallCount);
        Assert.Equal(2, clientFactory.CreatedClients.Count);
    }

    [Fact]
    public async Task EnsureSetupClientConnectedAsyncDisposesAllClientsOnWorkerConnectFailure()
    {
        RecordingClientFactory clientFactory = new([new(true), new(false)]);
        using LiveSendClientSession session = new(
            clientFactory.Create,
            new Uri("ws://localhost:12345/"),
            connectionCount: 2,
            workerConnectTimeoutMilliseconds: 1000,
            progressReporter: null);

        PlateauImportRequest request = CreateRequest();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.EnsureSetupClientConnectedAsync(request, CancellationToken.None));

        Assert.Equal(2, clientFactory.CreatedClients.Count);
        Assert.All(clientFactory.CreatedClients, client => Assert.True(client.Disposed));
        Assert.Null(session.SetupClient);
    }

    [Fact]
    public async Task EnsureSetupClientConnectedAsyncTreatsWorkerConnectTimeoutAsFatal()
    {
        RecordingClientFactory clientFactory = new([new(true), new(true, -1)]);
        using LiveSendClientSession session = new(
            clientFactory.Create,
            new Uri("ws://localhost:12345/"),
            connectionCount: 2,
            workerConnectTimeoutMilliseconds: 20,
            progressReporter: null);

        PlateauImportRequest request = CreateRequest();

        TimeoutException thrown = await Assert.ThrowsAsync<TimeoutException>(
            () => session.EnsureSetupClientConnectedAsync(request, CancellationToken.None));
        Assert.Contains("did not connect within", thrown.Message);

        Assert.Equal(2, clientFactory.CreatedClients.Count);
        Assert.All(clientFactory.CreatedClients, client => Assert.True(client.Disposed));
        Assert.Null(session.SetupClient);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public async Task CreateLaneClientAsyncRejectsLaneOutsideConfiguredCapacity(int laneIndex)
    {
        RecordingClientFactory clientFactory = new([new(true), new(true)]);
        using LiveSendClientSession session = new(
            clientFactory.Create,
            new Uri("ws://localhost:12345/"),
            connectionCount: 2,
            workerConnectTimeoutMilliseconds: 1000,
            progressReporter: null);

        PlateauImportRequest request = CreateRequest();

        await session.EnsureSetupClientConnectedAsync(request, CancellationToken.None);

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

    private sealed class RecordingClientFactory(IReadOnlyList<ConnectOutcome> connectOutcomes)
    {
        private int nextConnectOutcomeIndex;

        public List<RecordingResoniteLinkClient> CreatedClients { get; } = [];

        public RecordingResoniteLinkClient Create()
        {
            ConnectOutcome outcome = nextConnectOutcomeIndex < connectOutcomes.Count
                ? connectOutcomes[nextConnectOutcomeIndex]
                : new ConnectOutcome(true, 0);
            nextConnectOutcomeIndex++;

            RecordingResoniteLinkClient client = new(outcome.ConnectSucceeds, outcome.ConnectDelayMilliseconds);
            CreatedClients.Add(client);
            return client;
        }
    }

    private readonly record struct ConnectOutcome(bool ConnectSucceeds = true, int ConnectDelayMilliseconds = 0);

    private sealed class RecordingResoniteLinkClient(bool connectSucceeds, int connectDelayMilliseconds) : IResoniteLinkClient
    {
        private int nextSlotId;

        public int ConnectCallCount { get; private set; }

        public int AddSlotCallCount { get; private set; }

        public Uri? LastConnectedEndpoint { get; private set; }

        public bool Disposed { get; private set; }

        public async Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            ConnectCallCount++;
            LastConnectedEndpoint = endpoint;

            if (connectDelayMilliseconds < 0)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            else if (connectDelayMilliseconds > 0)
            {
                await Task.Delay(connectDelayMilliseconds, cancellationToken);
            }

            if (!connectSucceeds)
            {
                throw new InvalidOperationException("connect failed");
            }
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
