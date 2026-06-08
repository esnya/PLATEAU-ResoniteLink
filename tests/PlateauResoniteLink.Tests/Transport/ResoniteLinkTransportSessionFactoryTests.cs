using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using PlateauResoniteLink.Application.Importing;
using PlateauResoniteLink.Transport.ResoniteLink;

using TransportComponentLocator = PlateauResoniteLink.Transport.ResoniteLink.ResoniteTransportComponentLocator;
using TransportSlotLocator = PlateauResoniteLink.Transport.ResoniteLink.ResoniteTransportSlotLocator;

using ResoniteLink;

namespace PlateauResoniteLink.Tests.Transport;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test names describe contract cases.")]
public sealed class ResoniteLinkTransportSessionFactoryTests
{
    [Fact]
    public async Task Create_UsesMetricsWrapperWhenDiagnosticsEnabled()
    {
        RecordingClientFactory clientFactory = new();
        RecordingLogger logger = new();
        ResoniteLinkSendDiagnostics diagnostics = ResoniteLinkSendDiagnostics.CreateEnabled(logger);
        ILiveSendClientSession session = ResoniteLinkTransportSessionFactory.Create(
            new Uri("ws://localhost:12345/"),
            connectionCount: 1,
            diagnostics,
            logger,
            clientFactory.Create);

        try
        {
            diagnostics.StartSendWindow(connectionCount: 1);
            await session.EnsureConnectedAsync(CreateConnectionRequest(), CancellationToken.None);
            IResoniteLinkClient client = session.GetRequiredClient();
            ResoniteTransportSlotCreationResult slot = await client.AddSlotAsync(
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

            Assert.Equal(new TransportSlotLocator("slot-1"), slot.Slot);
            Assert.Single(clientFactory.CreatedClients);
            Assert.Equal(1, clientFactory.CreatedClients[0].ConnectCallCount);
            Assert.Equal(1, clientFactory.CreatedClients[0].AddSlotCallCount);
            Assert.Contains(logger.Messages, static message => message.Contains("rpc_breakdown", StringComparison.Ordinal));
            Assert.Contains(logger.Messages, static message => message.Contains("add_slot=1", StringComparison.Ordinal));
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

        public RecordingResoniteLinkClient Create(ILogger logger)
        {
            _ = logger;
            RecordingResoniteLinkClient client = new();
            CreatedClients.Add(client);
            return client;
        }
    }

    private sealed class RecordingLogger : ILogger
    {
        private sealed class Scope : IDisposable
        {
            public static readonly Scope Instance = new();

            public void Dispose()
            {
            }
        }

        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            _ = state;
            return Scope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            _ = logLevel;
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _ = logLevel;
            _ = eventId;
            _ = exception;
            Messages.Add(formatter(state, exception));
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

        public Task<ResoniteTransportComponentCreationResult> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ResoniteTransportSlotCreationResult> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
        {
            AddSlotCallCount++;
            return Task.FromResult(new ResoniteTransportSlotCreationResult(new TransportSlotLocator("slot-1")));
        }

        public Task<BatchResponse> RunDataModelOperationBatchAsync(
            IReadOnlyList<DataModelOperation> operations,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
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

        public Task<Uri> ImportTextureAsync(ITextureImportSource textureSource, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task UpdateComponentAsync(ResoniteComponentUpdate request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
