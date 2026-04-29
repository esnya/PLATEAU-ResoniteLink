using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Transport.ResoniteLink;

using TransportComponentLocator = PlateauResoniteLink.Transport.ResoniteLink.ResoniteTransportComponentLocator;
using TransportSlotLocator = PlateauResoniteLink.Transport.ResoniteLink.ResoniteTransportSlotLocator;

using ResoniteLink;

namespace PlateauResoniteLink.Tests.Transport;

public sealed class LiveSendClientSessionTests
{
    [Fact]
    public async Task EnsureConnectedAsyncConnectsAllConfiguredSessionsEagerly()
    {
        RecordingClientFactory clientFactory = new([new(true), new(true), new(true)]);
        using LiveSendClientSession session = new(
            clientFactory.Create,
            new Uri("ws://localhost:12345/"),
            connectionCount: 3,
            ResoniteLinkSendDiagnostics.Disabled,
            progressReporter: null);

        await session.EnsureConnectedAsync(CreateConnectionRequest(), CancellationToken.None);

        Assert.Equal(3, clientFactory.CreatedClients.Count);
        Assert.NotNull(session.GetRequiredClient());
        Assert.All(
            clientFactory.CreatedClients,
            client => Assert.Equal(1, client.ConnectCallCount));
    }

    [Fact]
    public async Task RoutedClientPinsMutationAndAssetCallsToPrimaryClient()
    {
        RecordingClientFactory clientFactory = new([new(true), new(true), new(true)]);
        using LiveSendClientSession session = new(
            clientFactory.Create,
            new Uri("ws://localhost:12345/"),
            connectionCount: 3,
            ResoniteLinkSendDiagnostics.Disabled,
            progressReporter: null);

        await session.EnsureConnectedAsync(CreateConnectionRequest(), CancellationToken.None);
        IResoniteLinkClient routedClient = session.GetRequiredClient();

        for (int callIndex = 0; callIndex < 6; callIndex++)
        {
            await routedClient.RunDataModelOperationBatchAsync([], CancellationToken.None);
            await routedClient.ImportMeshAsync(
                new ImportMeshRawData
                {
                    RawBinaryPayload = [1, 2, 3],
                    VertexCount = 3,
                },
                CancellationToken.None);
            await routedClient.ImportTextureAsync(
                new ResoniteRawTextureImport(
                    1,
                    1,
                    ResoniteTextureColorProfiles.Srgb,
                    [255, 255, 255, 255]),
                CancellationToken.None);
        }

        Assert.Equal(6, clientFactory.CreatedClients.Sum(static client => client.BatchCallCount));
        Assert.Equal(6, clientFactory.CreatedClients[0].BatchCallCount);
        Assert.Equal(6, clientFactory.CreatedClients[0].ImportMeshCallCount);
        Assert.Equal(6, clientFactory.CreatedClients[0].ImportTextureCallCount);
        Assert.All(
            clientFactory.CreatedClients.Skip(1),
            client =>
            {
                Assert.Equal(0, client.BatchCallCount);
                Assert.Equal(0, client.ImportMeshCallCount);
                Assert.Equal(0, client.ImportTextureCallCount);
            });
    }

    [Fact]
    public async Task RoutedClientPinsAuthoritativeCallsToPrimaryClient()
    {
        RecordingClientFactory clientFactory = new([new(true), new(true), new(true)]);
        using LiveSendClientSession session = new(
            clientFactory.Create,
            new Uri("ws://localhost:12345/"),
            connectionCount: 3,
            ResoniteLinkSendDiagnostics.Disabled,
            progressReporter: null);

        await session.EnsureConnectedAsync(CreateConnectionRequest(), CancellationToken.None);
        IResoniteLinkClient routedClient = session.GetRequiredClient();

        for (int callIndex = 0; callIndex < 6; callIndex++)
        {
            await ((IResoniteLinkClient)routedClient).AddSlotAsync(CreateSlotRequest(), CancellationToken.None);
        }

        Assert.Equal(6, clientFactory.CreatedClients.Sum(static client => client.AddSlotCallCount));
        Assert.Equal(6, clientFactory.CreatedClients[0].AddSlotCallCount);
        Assert.All(clientFactory.CreatedClients.Skip(1), client => Assert.Equal(0, client.AddSlotCallCount));
    }

    [Fact]
    public async Task RoutedClientConnectAsyncConnectsAllRoutesBeforeAuthoritativeCalls()
    {
        using RecordingResoniteLinkClient firstClient = new(true, 0);
        using RecordingResoniteLinkClient secondClient = new(true, 0);
        using RecordingResoniteLinkClient thirdClient = new(true, 0);
        using RoutedResoniteLinkClient routedClient = new([firstClient, secondClient, thirdClient]);

        await routedClient.ConnectAsync(new Uri("ws://localhost:12345/"), CancellationToken.None);
        await routedClient.GetSlotAsync(new TransportSlotLocator("slot-a"), 0, CancellationToken.None);
        await routedClient.GetSlotAsync(new TransportSlotLocator("slot-b"), 0, CancellationToken.None);
        await routedClient.GetSlotAsync(new TransportSlotLocator("slot-c"), 0, CancellationToken.None);

        Assert.Equal(1, firstClient.ConnectCallCount);
        Assert.Equal(1, secondClient.ConnectCallCount);
        Assert.Equal(1, thirdClient.ConnectCallCount);
        Assert.Equal(3, firstClient.GetSlotCallCount);
        Assert.Equal(0, secondClient.GetSlotCallCount);
        Assert.Equal(0, thirdClient.GetSlotCallCount);
    }

    [Fact]
    public async Task EnsureConnectedAsyncDisposesAllClientsOnConnectFailure()
    {
        RecordingClientFactory clientFactory = new([new(true), new(false)]);
        using LiveSendClientSession session = new(
            clientFactory.Create,
            new Uri("ws://localhost:12345/"),
            connectionCount: 2,
            ResoniteLinkSendDiagnostics.Disabled,
            progressReporter: null);

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.EnsureConnectedAsync(CreateConnectionRequest(), CancellationToken.None));
        Assert.Contains("connect failed", thrown.Message);

        Assert.Equal(2, clientFactory.CreatedClients.Count);
        Assert.All(clientFactory.CreatedClients, client => Assert.True(client.Disposed));
        Assert.Throws<InvalidOperationException>(session.GetRequiredClient);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task EnsureConnectedAsyncRejectsNonPositiveConnectionCount(int connectionCount)
    {
        RecordingClientFactory clientFactory = new([]);
        using LiveSendClientSession session = new(
            clientFactory.Create,
            new Uri("ws://localhost:12345/"),
            connectionCount,
            ResoniteLinkSendDiagnostics.Disabled,
            progressReporter: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.EnsureConnectedAsync(CreateConnectionRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task ResetClientsAsyncDisposesConnectedRoutesAndAllowsFreshReconnect()
    {
        RecordingClientFactory clientFactory = new([new(true), new(true), new(true), new(true)]);
        using LiveSendClientSession session = new(
            clientFactory.Create,
            new Uri("ws://localhost:12345/"),
            connectionCount: 2,
            ResoniteLinkSendDiagnostics.Disabled,
            progressReporter: null);

        LiveSendConnectionRequest connectionRequest = CreateConnectionRequest();

        await session.EnsureConnectedAsync(connectionRequest, CancellationToken.None);
        RecordingResoniteLinkClient[] firstClients = clientFactory.CreatedClients.ToArray();

        await session.ResetClientsAsync(CancellationToken.None);

        Assert.Throws<InvalidOperationException>(session.GetRequiredClient);
        Assert.All(firstClients, client => Assert.True(client.Disposed));

        await session.EnsureConnectedAsync(connectionRequest, CancellationToken.None);

        Assert.Equal(4, clientFactory.CreatedClients.Count);
        Assert.NotNull(session.GetRequiredClient());
        Assert.All(clientFactory.CreatedClients.Skip(2), client => Assert.Equal(1, client.ConnectCallCount));
    }

    private static LiveSendConnectionRequest CreateConnectionRequest()
    {
        return new LiveSendConnectionRequest(
            Dataset: "tokyo23ku",
            MeshCode: "53394525");
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

        public int BatchCallCount { get; private set; }

        public int GetSlotCallCount { get; private set; }

        public int ImportMeshCallCount { get; private set; }

        public int ImportTextureCallCount { get; private set; }

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

        public Task<ResoniteTransportComponentCreationResult> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ResoniteTransportSlotCreationResult> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddSlotCallCount++;
            return Task.FromResult(
                new ResoniteTransportSlotCreationResult(
                    new TransportSlotLocator($"srv_slot_{Interlocked.Increment(ref nextSlotId)}")));
        }

        public Task<BatchResponse> RunDataModelOperationBatchAsync(
            IReadOnlyList<DataModelOperation> operations,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BatchCallCount++;
            return Task.FromResult(new BatchResponse
            {
                Success = true,
                Responses = [],
            });
        }

        public Task<Component?> GetComponentAsync(TransportComponentLocator component, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<Slot?> GetSlotAsync(TransportSlotLocator slot, int depth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetSlotCallCount++;
            return Task.FromResult<Slot?>(new Slot
            {
                ID = slot.Value,
            });
        }

        public Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImportMeshCallCount++;
            return Task.FromResult(new Uri("resdb:///mesh"));
        }

        public Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImportTextureCallCount++;
            return Task.FromResult(new Uri("resdb:///texture"));
        }

        public Task UpdateComponentAsync(ResoniteComponentUpdate request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}


