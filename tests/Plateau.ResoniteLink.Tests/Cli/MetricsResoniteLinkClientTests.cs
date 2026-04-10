using System.Diagnostics.CodeAnalysis;

using Plateau.ResoniteLink.Cli;

using ResoniteLink;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class MetricsResoniteLinkClientTests
{
    [Fact]
    public async Task MethodsRecordRpcBreakdownAndDelegateToInnerClient()
    {
        List<string> progressMessages = [];
        ResoniteLinkSendDiagnostics diagnostics = ResoniteLinkSendDiagnostics.CreateEnabled(progressMessages.Add);
        using RecordingResoniteLinkClient inner = new();
        using MetricsResoniteLinkClient client = new(inner, diagnostics);

        diagnostics.StartSendWindow(connectionCount: 2);

        Uri endpoint = new("ws://localhost:12345/", UriKind.Absolute);
        await client.ConnectAsync(endpoint, CancellationToken.None);
        string componentId = await client.AddComponentAsync(
            new AddComponent
            {
                ContainerSlotId = "slot-id",
                Data = new Component
                {
                    ID = "component-id",
                    ComponentType = "component-type",
                },
            },
            CancellationToken.None);
        string slotId = await client.AddSlotAsync(
            new AddSlot
            {
                Data = new Slot
                {
                    ID = "slot-id",
                    Name = new Field_string
                    {
                        Value = "Slot",
                    },
                },
            },
            CancellationToken.None);
        await client.RunDataModelOperationBatchAsync([], CancellationToken.None);
        Component? component = await client.GetComponentAsync("component-id", CancellationToken.None);
        Slot? slot = await client.GetSlotAsync("slot-id", 1, CancellationToken.None);
        Uri meshUri = await client.ImportMeshAsync(
            new ImportMeshRawData
            {
                RawBinaryPayload = [1, 2, 3],
                VertexCount = 3,
            },
            CancellationToken.None);
        Uri textureUri = await client.ImportTextureAsync(
            new ResoniteFileTextureImport("/tmp/texture.png"),
            CancellationToken.None);
        await client.UpdateComponentAsync(
            new UpdateComponent
            {
                Data = new Component
                {
                    ID = "component-id",
                },
            },
            CancellationToken.None);

        diagnostics.CompleteSendWindow();

        Assert.Equal(endpoint, inner.ConnectedEndpoint);
        Assert.Equal("component_1", componentId);
        Assert.Equal("slot_1", slotId);
        Assert.Equal("component-id", component?.ID);
        Assert.Equal("slot-id", slot?.ID);
        Assert.Equal(new Uri("resdb:///mesh/1", UriKind.Absolute), meshUri);
        Assert.Equal(new Uri("resdb:///texture/1", UriKind.Absolute), textureUri);
        Assert.Contains(
            progressMessages,
            static message => message.Contains(
                "rpc_breakdown add_component=1, add_slot=1, batch=1, connect=1, get_component=1, get_slot=1, import_mesh=1, import_texture=1, update_component=1",
                StringComparison.Ordinal));
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The wrapper under test owns the inner client lifetime.")]
    [Fact]
    public void DisposeDelegatesToInnerClient()
    {
        ResoniteLinkSendDiagnostics diagnostics = ResoniteLinkSendDiagnostics.CreateEnabled();
        using RecordingResoniteLinkClient inner = new();
        using MetricsResoniteLinkClient client = new(inner, diagnostics);

        client.Dispose();

        Assert.True(inner.DisposeCalled);
    }

    private sealed class RecordingResoniteLinkClient : IResoniteLinkClient
    {
        private int nextComponentId;
        private int nextSlotId;
        private int nextMeshId;
        private int nextTextureId;

        public Uri? ConnectedEndpoint { get; private set; }

        public bool DisposeCalled { get; private set; }

        public void Dispose()
        {
            DisposeCalled = true;
        }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectedEndpoint = endpoint;
            return Task.CompletedTask;
        }

        public Task<string> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult($"component_{Interlocked.Increment(ref nextComponentId)}");
        }

        public Task<string> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult($"slot_{Interlocked.Increment(ref nextSlotId)}");
        }

        public Task RunDataModelOperationBatchAsync(
            IReadOnlyList<DataModelOperation> operations,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Component?>(new Component
            {
                ID = componentId,
            });
        }

        public Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Slot?>(new Slot
            {
                ID = slotId,
            });
        }

        public Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new Uri($"resdb:///mesh/{Interlocked.Increment(ref nextMeshId)}", UriKind.Absolute));
        }

        public Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new Uri($"resdb:///texture/{Interlocked.Increment(ref nextTextureId)}", UriKind.Absolute));
        }

        public Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
