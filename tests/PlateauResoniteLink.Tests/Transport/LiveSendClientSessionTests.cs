using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Domain.Importing;
using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Transport.ResoniteLink;

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

        PlateauImportRequest request = CreateRequest();

        await session.EnsureConnectedAsync(request, CancellationToken.None);

        Assert.Equal(3, clientFactory.CreatedClients.Count);
        Assert.NotNull(session.RoutedClient);
        Assert.All(
            clientFactory.CreatedClients,
            client => Assert.Equal(1, client.ConnectCallCount));
    }

    [Fact]
    public async Task RoutedClientDistributesBatchCallsAcrossConnectedClients()
    {
        RecordingClientFactory clientFactory = new([new(true), new(true), new(true)]);
        using LiveSendClientSession session = new(
            clientFactory.Create,
            new Uri("ws://localhost:12345/"),
            connectionCount: 3,
            ResoniteLinkSendDiagnostics.Disabled,
            progressReporter: null);

        PlateauImportRequest request = CreateRequest();

        await session.EnsureConnectedAsync(request, CancellationToken.None);
        IResoniteLinkClient routedClient = Assert.IsAssignableFrom<IResoniteLinkClient>(session.RoutedClient);

        for (int callIndex = 0; callIndex < 6; callIndex++)
        {
            await routedClient.RunDataModelOperationBatchAsync([], CancellationToken.None);
        }

        Assert.Equal(6, clientFactory.CreatedClients.Sum(static client => client.BatchCallCount));
        Assert.All(clientFactory.CreatedClients, client => Assert.True(client.BatchCallCount > 0));
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

        PlateauImportRequest request = CreateRequest();

        await session.EnsureConnectedAsync(request, CancellationToken.None);
        IResoniteLinkClient routedClient = Assert.IsAssignableFrom<IResoniteLinkClient>(session.RoutedClient);

        for (int callIndex = 0; callIndex < 6; callIndex++)
        {
            await ((IResoniteLinkClient)routedClient).AddSlotAsync(CreateSlotRequest(), CancellationToken.None);
        }

        Assert.Equal(6, clientFactory.CreatedClients.Sum(static client => client.AddSlotCallCount));
        Assert.Equal(6, clientFactory.CreatedClients[0].AddSlotCallCount);
        Assert.All(clientFactory.CreatedClients.Skip(1), client => Assert.Equal(0, client.AddSlotCallCount));
    }

    [Fact]
    public async Task RoutedClientConnectAsyncConnectsAllRoutesBeforeBalancedCalls()
    {
        RecordingResoniteLinkClient firstClient = new(true, 0);
        RecordingResoniteLinkClient secondClient = new(true, 0);
        RecordingResoniteLinkClient thirdClient = new(true, 0);
        using RoutedResoniteLinkClient routedClient = new([firstClient, secondClient, thirdClient]);

        await routedClient.ConnectAsync(new Uri("ws://localhost:12345/"), CancellationToken.None);
        await routedClient.RunDataModelOperationBatchAsync([], CancellationToken.None);
        await routedClient.RunDataModelOperationBatchAsync([], CancellationToken.None);
        await routedClient.RunDataModelOperationBatchAsync([], CancellationToken.None);

        Assert.Equal(1, firstClient.ConnectCallCount);
        Assert.Equal(1, secondClient.ConnectCallCount);
        Assert.Equal(1, thirdClient.ConnectCallCount);
        Assert.All(new[] { firstClient, secondClient, thirdClient }, client => Assert.True(client.BatchCallCount > 0));
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

        PlateauImportRequest request = CreateRequest();

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.EnsureConnectedAsync(request, CancellationToken.None));
        Assert.Contains("connect failed", thrown.Message);

        Assert.Equal(2, clientFactory.CreatedClients.Count);
        Assert.All(clientFactory.CreatedClients, client => Assert.True(client.Disposed));
        Assert.Null(session.RoutedClient);
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

        PlateauImportRequest request = CreateRequest();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.EnsureConnectedAsync(request, CancellationToken.None));
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

        PlateauImportRequest request = CreateRequest();

        await session.EnsureConnectedAsync(request, CancellationToken.None);
        RecordingResoniteLinkClient[] firstClients = clientFactory.CreatedClients.ToArray();

        await session.ResetClientsAsync(CancellationToken.None);

        Assert.Null(session.RoutedClient);
        Assert.All(firstClients, client => Assert.True(client.Disposed));

        await session.EnsureConnectedAsync(request, CancellationToken.None);

        Assert.Equal(4, clientFactory.CreatedClients.Count);
        Assert.NotNull(session.RoutedClient);
        Assert.All(clientFactory.CreatedClients.Skip(2), client => Assert.Equal(1, client.ConnectCallCount));
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

        public int BatchCallCount { get; private set; }

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
            cancellationToken.ThrowIfCancellationRequested();
            BatchCallCount++;
            return Task.FromResult(new BatchResponse
            {
                Success = true,
                Responses = [],
            });
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
