using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

using PlateauResoniteLink.Targets.Resonite;
using PlateauResoniteLink.Transport.ResoniteLink;

using ResoniteLink;

namespace PlateauResoniteLink.Tests.Transport;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class ResoniteLinkTransportSessionFactoryTests
{
    [Fact]
    public async Task Create_UsesMetricsWrapperWhenDiagnosticsEnabled()
    {
        RecordingClientFactory clientFactory = new();
        List<string> messages = [];
        ResoniteLinkSendDiagnostics diagnostics = ResoniteLinkSendDiagnostics.CreateEnabled(messages.Add);
        ILiveSendClientSession session = ResoniteLinkTransportSessionFactory.Create(
            new Uri("ws://localhost:12345/"),
            connectionCount: 1,
            diagnostics,
            progressReporter: null,
            clientFactory.Create);

        try
        {
            diagnostics.StartSendWindow(connectionCount: 1);
            await session.EnsureConnectedAsync(CreateConnectionRequest(), CancellationToken.None);
            IResoniteLinkClient routedClient = session.GetRequiredClient();
            string slotId = await routedClient.AddSlotAsync(
                new AddSlot
                {
                    Data = new Slot
                    {
                        Name = new Field_string
                        {
                            Value = "metrics-check",
                        },
                        Parent = new Reference
                        {
                            TargetID = "Root",
                        },
                    },
                },
                CancellationToken.None);
            diagnostics.CompleteSendWindow();

            Assert.Equal("slot-1", slotId);
            Assert.Single(clientFactory.CreatedClients);
            Assert.Equal(1, clientFactory.CreatedClients[0].ConnectCallCount);
            Assert.Equal(1, clientFactory.CreatedClients[0].AddSlotCallCount);
            Assert.Contains(messages, static message => message.Contains("rpc_breakdown", StringComparison.Ordinal));
            Assert.Contains(messages, static message => message.Contains("add_slot=1", StringComparison.Ordinal));
        }
        finally
        {
            session.DisposeClients();
        }

        Assert.Single(clientFactory.CreatedClients);
        Assert.Equal(1, clientFactory.CreatedClients[0].DisposeCallCount);
    }

    private static LiveSendConnectionRequest CreateConnectionRequest()
    {
        return new LiveSendConnectionRequest(
            Dataset: "tokyo23ku",
            MeshCode: "53394525");
    }

    private sealed class RecordingClientFactory
    {
        public List<RecordingResoniteLinkClient> CreatedClients { get; } = [];

        public RecordingResoniteLinkClient Create()
        {
            RecordingResoniteLinkClient client = new();
            CreatedClients.Add(client);
            return client;
        }
    }

    private sealed class RecordingResoniteLinkClient : IResoniteLinkClient
    {
        public int ConnectCallCount { get; private set; }

        public int AddSlotCallCount { get; private set; }

        public int DisposeCallCount { get; private set; }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            ConnectCallCount++;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            DisposeCallCount++;
        }

        public Task<string> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<string> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
        {
            AddSlotCallCount++;
            return Task.FromResult("slot-1");
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
