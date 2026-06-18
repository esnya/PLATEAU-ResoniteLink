using PlateauResoniteLink.Core.Application.Importing.Contracts;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


using PlateauResoniteLink.Resonite.Transport.ResoniteLink;

using TransportComponentLocator = PlateauResoniteLink.Resonite.Transport.ResoniteLink.ResoniteTransportComponentLocator;
using TransportSlotLocator = PlateauResoniteLink.Resonite.Transport.ResoniteLink.ResoniteTransportSlotLocator;

using ResoniteLink;

using static PlateauResoniteLink.Tests.TextureImportSourceTestFactory;

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
            ResoniteLinkSendDiagnostics.Disabled);

        await session.EnsureConnectedAsync(CreateConnectionRequest(), CancellationToken.None);

        Assert.Equal(3, clientFactory.CreatedClients.Count);
        Assert.NotNull(session.GetRequiredClient());
        Assert.All(
            clientFactory.CreatedClients,
            client => Assert.Equal(1, client.ConnectCallCount));
    }

    [Fact]
    public async Task LoadBalancingClientPinsBatchCallsToSessionStateConnection()
    {
        RecordingClientFactory clientFactory = new([new(true), new(true), new(true)]);
        using LiveSendClientSession session = new(
            clientFactory.Create,
            new Uri("ws://localhost:12345/"),
            connectionCount: 3,
            ResoniteLinkSendDiagnostics.Disabled);

        await session.EnsureConnectedAsync(CreateConnectionRequest(), CancellationToken.None);
        IResoniteLinkClient client = session.GetRequiredClient();

        for (int callIndex = 0; callIndex < 6; callIndex++)
        {
            await client.RunDataModelOperationBatchAsync([], CancellationToken.None);
        }

        Assert.Equal(6, clientFactory.CreatedClients.Sum(static client => client.BatchCallCount));
        Assert.Equal(6, clientFactory.CreatedClients[0].BatchCallCount);
        Assert.All(clientFactory.CreatedClients.Skip(1), client => Assert.Equal(0, client.BatchCallCount));
    }

    [Fact]
    public async Task LoadBalancingClientPinsMutationCallsToSessionStateConnection()
    {
        RecordingClientFactory clientFactory = new([new(true), new(true), new(true)]);
        using LiveSendClientSession session = new(
            clientFactory.Create,
            new Uri("ws://localhost:12345/"),
            connectionCount: 3,
            ResoniteLinkSendDiagnostics.Disabled);

        await session.EnsureConnectedAsync(CreateConnectionRequest(), CancellationToken.None);
        IResoniteLinkClient client = session.GetRequiredClient();

        for (int callIndex = 0; callIndex < 6; callIndex++)
        {
            await ((IResoniteLinkClient)client).AddSlotAsync(CreateSlotRequest(), CancellationToken.None);
        }

        Assert.Equal(6, clientFactory.CreatedClients.Sum(static client => client.AddSlotCallCount));
        Assert.Equal(6, clientFactory.CreatedClients[0].AddSlotCallCount);
        Assert.All(clientFactory.CreatedClients.Skip(1), client => Assert.Equal(0, client.AddSlotCallCount));
    }

    [Fact]
    public async Task LoadBalancingClientPrefersLeastBusyRoutesAndAllowsParallelRoutes()
    {
        RecordingClientFactory clientFactory = new([new(true), new(true), new(true)]);
        using LiveSendClientSession session = new(
            clientFactory.Create,
            new Uri("ws://localhost:12345/"),
            connectionCount: 3,
            ResoniteLinkSendDiagnostics.Disabled);

        await session.EnsureConnectedAsync(CreateConnectionRequest(), CancellationToken.None);
        IResoniteLinkClient client = session.GetRequiredClient();

        Task<Uri>[] imports = Enumerable.Range(0, 6)
            .Select(_ => client.ImportTextureAsync(
                CreateRawTextureSource(1, 1, ResoniteTextureColorProfiles.Srgb, [255, 255, 255, 255]),
                CancellationToken.None))
            .ToArray();

        await Task.WhenAll(imports);

        Assert.Equal(6, clientFactory.CreatedClients.Sum(static client => client.TextureImportCallCount));
        Assert.True(clientFactory.MaxConcurrentTextureImports > 1);
        Assert.All(clientFactory.CreatedClients, client => Assert.Equal(2, client.TextureImportCallCount));
    }

    [Fact]
    public async Task LoadBalancingClientConnectAsyncConnectsAllConnectionsBeforeCalls()
    {
        using RecordingResoniteLinkClient firstClient = new(true, 0);
        using RecordingResoniteLinkClient secondClient = new(true, 0);
        using RecordingResoniteLinkClient thirdClient = new(true, 0);
        using LoadBalancingResoniteLinkClient loadBalancedClient = new([firstClient, secondClient, thirdClient]);

        await loadBalancedClient.ConnectAsync(new Uri("ws://localhost:12345/"), CancellationToken.None);
        await loadBalancedClient.RunDataModelOperationBatchAsync([], CancellationToken.None);
        await loadBalancedClient.RunDataModelOperationBatchAsync([], CancellationToken.None);
        await loadBalancedClient.RunDataModelOperationBatchAsync([], CancellationToken.None);

        Assert.Equal(1, firstClient.ConnectCallCount);
        Assert.Equal(1, secondClient.ConnectCallCount);
        Assert.Equal(1, thirdClient.ConnectCallCount);
        Assert.Equal(3, firstClient.BatchCallCount);
        Assert.Equal(0, secondClient.BatchCallCount);
        Assert.Equal(0, thirdClient.BatchCallCount);
    }

    [Fact]
    public async Task LoadBalancingClientPinsStateCallsToSessionStateConnection()
    {
        using RecordingResoniteLinkClient firstClient = new(true, 0);
        using RecordingResoniteLinkClient secondClient = new(true, 0);
        using LoadBalancingResoniteLinkClient loadBalancedClient = new(
            [firstClient, secondClient]);

        await loadBalancedClient.ConnectAsync(new Uri("ws://localhost:12345/"), CancellationToken.None);
        await loadBalancedClient.RunDataModelOperationBatchAsync([], CancellationToken.None);
        await loadBalancedClient.RunDataModelOperationBatchAsync([], CancellationToken.None);

        Assert.Equal(1, firstClient.ConnectCallCount);
        Assert.Equal(1, secondClient.ConnectCallCount);
        Assert.Equal(2, firstClient.BatchCallCount);
        Assert.Equal(0, secondClient.BatchCallCount);
    }

    [Fact]
    public async Task EnsureConnectedAsyncDisposesAllClientsOnConnectFailure()
    {
        RecordingClientFactory clientFactory = new([new(true), new(false)]);
        using LiveSendClientSession session = new(
            clientFactory.Create,
            new Uri("ws://localhost:12345/"),
            connectionCount: 2,
            ResoniteLinkSendDiagnostics.Disabled);

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
            ResoniteLinkSendDiagnostics.Disabled);

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
            ResoniteLinkSendDiagnostics.Disabled);

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
        private int maxConcurrentTextureImports;

        public List<RecordingResoniteLinkClient> CreatedClients { get; } = [];

        public int MaxConcurrentTextureImports => Volatile.Read(ref maxConcurrentTextureImports);

        private int activeTextureImports;

        public RecordingResoniteLinkClient Create()
        {
            ConnectOutcome outcome = nextConnectOutcomeIndex < connectOutcomes.Count
                ? connectOutcomes[nextConnectOutcomeIndex]
                : new ConnectOutcome(true, 0);
            nextConnectOutcomeIndex++;

            RecordingResoniteLinkClient client = new(outcome.ConnectSucceeds, outcome.ConnectDelayMilliseconds, this);
            CreatedClients.Add(client);
            return client;
        }

        public void RecordTextureImportStarted()
        {
            int active = Interlocked.Increment(ref activeTextureImports);
            RecordMax(ref maxConcurrentTextureImports, active);
        }

        public void RecordTextureImportCompleted()
        {
            Interlocked.Decrement(ref activeTextureImports);
        }
    }

    private readonly record struct ConnectOutcome(bool ConnectSucceeds = true, int ConnectDelayMilliseconds = 0);

    private sealed class RecordingResoniteLinkClient(
        bool connectSucceeds,
        int connectDelayMilliseconds,
        RecordingClientFactory? owner = null) : IResoniteLinkClient
    {
        private int nextSlotId;
        private int maxConcurrentTextureImports;

        public int ConnectCallCount { get; private set; }

        public int AddSlotCallCount { get; private set; }

        public int BatchCallCount { get; private set; }

        public int TextureImportCallCount { get; private set; }

        public int MaxConcurrentTextureImports => Volatile.Read(ref maxConcurrentTextureImports);

        public Uri? LastConnectedEndpoint { get; private set; }

        public bool Disposed { get; private set; }

        private int activeTextureImports;
        private int nextTextureId;

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
            throw new NotSupportedException();
        }

        public Task<Uri> ImportMeshAsync(IGeometryImportSource geometrySource, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public async Task<Uri> ImportTextureAsync(ITextureImportSource textureSource, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TextureImportCallCount++;
            int active = Interlocked.Increment(ref activeTextureImports);
            RecordMax(ref maxConcurrentTextureImports, active);
            owner?.RecordTextureImportStarted();
            try
            {
                await Task.Delay(25, cancellationToken);
                int textureId = Interlocked.Increment(ref nextTextureId) - 1;
                return new Uri($"resdb:///texture/{textureId}", UriKind.Absolute);
            }
            finally
            {
                Interlocked.Decrement(ref activeTextureImports);
                owner?.RecordTextureImportCompleted();
            }
        }

        public Task UpdateComponentAsync(ResoniteComponentUpdate request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private static void RecordMax(ref int target, int value)
    {
        int observed;
        do
        {
            observed = Volatile.Read(ref target);
            if (value <= observed)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, value, observed) != observed);
    }
}
