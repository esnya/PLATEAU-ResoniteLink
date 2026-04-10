using System.Diagnostics.CodeAnalysis;

using Plateau.ResoniteLink.Cli;

using ResoniteLink;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class ResoniteLinkClientTests
{
    [Fact]
    public void EnsureSuccessThrowsProtocolErrorWhenResponseIsNull()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ResoniteLinkClient.EnsureSuccess(null, "add component '[FrooxEngine]FrooxEngine.MeshRenderer' on 'slot-1'"));

        Assert.Equal(
            "ResoniteLink add component '[FrooxEngine]FrooxEngine.MeshRenderer' on 'slot-1' returned a null response.",
            exception.Message);
    }

    [Fact]
    public void EnsureSuccessThrowsErrorInfoWhenResponseFails()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ResoniteLinkClient.EnsureSuccess(
                new Response
                {
                    Success = false,
                    ErrorInfo = "server said no",
                },
                "add slot 'Assets' on 'Root'"));

        Assert.Equal(
            "ResoniteLink add slot 'Assets' on 'Root' failed: server said no",
            exception.Message);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The client under test owns the transport lifetime.")]
    [Fact]
    public async Task AddComponentAsyncRejectsPreCanceledTokenBeforeCallingTransport()
    {
        using CancellationTokenSource cancellationTokenSource = new();
        using FakeResoniteLinkTransport transport = new();
        using ResoniteLinkClient client = new(transport);
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.AddComponentAsync(
            new AddComponent
            {
                ContainerSlotId = "slot-1",
                Data = new Component
                {
                    ComponentType = "[FrooxEngine]FrooxEngine.MeshRenderer",
                },
            },
            cancellationTokenSource.Token));
        Assert.Equal(0, transport.AddComponentCallCount);
    }

    [Fact]
    public async Task UpdateComponentAsyncRejectsPreCanceledTokenBeforeCallingTransport()
    {
        using CancellationTokenSource cancellationTokenSource = new();
        using FakeResoniteLinkTransport transport = new();
        using ResoniteLinkClient client = new(transport);
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.UpdateComponentAsync(
            new UpdateComponent
            {
                Data = new Component
                {
                    ID = "component-1",
                },
            },
            cancellationTokenSource.Token));
        Assert.Equal(0, transport.UpdateComponentCallCount);
    }

    [Fact]
    public async Task AddComponentAsyncCancelsInFlightTransportCall()
    {
        using CancellationTokenSource cancellationTokenSource = new();
        using FakeResoniteLinkTransport transport = new();
        using ResoniteLinkClient client = new(transport);

        Task<string> pending = client.AddComponentAsync(
            new AddComponent
            {
                ContainerSlotId = "slot-1",
                Data = new Component
                {
                    ComponentType = "[FrooxEngine]FrooxEngine.MeshRenderer",
                },
            },
            cancellationTokenSource.Token);
        await transport.AddComponentStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
    }

    [Fact]
    public async Task GetSlotAsyncCancelsInFlightTransportCall()
    {
        using CancellationTokenSource cancellationTokenSource = new();
        using FakeResoniteLinkTransport transport = new();
        using ResoniteLinkClient client = new(transport);

        Task<Slot?> pending = client.GetSlotAsync("slot-1", 1, cancellationTokenSource.Token);
        await transport.GetSlotStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
    }

    [Fact]
    public async Task ImportMeshAsyncCancelsInFlightTransportCall()
    {
        using CancellationTokenSource cancellationTokenSource = new();
        using FakeResoniteLinkTransport transport = new();
        using ResoniteLinkClient client = new(transport);

        Task<Uri> pending = client.ImportMeshAsync(
            new ImportMeshRawData
            {
                RawBinaryPayload = [1, 2, 3],
                VertexCount = 3,
            },
            cancellationTokenSource.Token);
        await transport.ImportMeshStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The client under test owns the transport lifetime.")]
    [Fact]
    public void DisposeDisposesTransport()
    {
        using FakeResoniteLinkTransport transport = new();
        using ResoniteLinkClient client = new(transport);

        client.Dispose();

        Assert.True(transport.IsDisposed);
    }

    private sealed class FakeResoniteLinkTransport : IResoniteLinkTransport, IDisposable
    {
        private readonly TaskCompletionSource<NewEntityId> addComponentCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<SlotData> getSlotCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<AssetData> importMeshCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int AddComponentCallCount { get; private set; }

        public int UpdateComponentCallCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public TaskCompletionSource AddComponentStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource GetSlotStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ImportMeshStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<NewEntityId> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
        {
            AddComponentCallCount++;
            AddComponentStarted.TrySetResult();
            return addComponentCompletion.Task.WaitAsync(cancellationToken);
        }

        public Task<NewEntityId> AddSlotAsync(AddSlot request, CancellationToken cancellationToken) => Task.FromResult(
            new NewEntityId
            {
                Success = true,
                EntityId = "slot-1",
            });

        public Task<BatchResponse> RunDataModelOperationBatchAsync(IReadOnlyList<DataModelOperation> operations, CancellationToken cancellationToken) => Task.FromResult(
            new BatchResponse
            {
                Success = true,
            });

        public Task<ComponentData> GetComponentDataAsync(GetComponent request, CancellationToken cancellationToken) => Task.FromResult(
            new ComponentData
            {
                Success = true,
            });

        public Task<SlotData> GetSlotDataAsync(GetSlot request, CancellationToken cancellationToken)
        {
            GetSlotStarted.TrySetResult();
            return getSlotCompletion.Task.WaitAsync(cancellationToken);
        }

        public Task<AssetData> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
        {
            ImportMeshStarted.TrySetResult();
            return importMeshCompletion.Task.WaitAsync(cancellationToken);
        }

        public Task<AssetData> ImportTextureAsync(ImportTexture2DFile request, CancellationToken cancellationToken) => Task.FromResult(
            new AssetData
            {
                Success = true,
                AssetURL = new Uri("resonite://texture", UriKind.Absolute),
            });

        public Task<AssetData> ImportTextureAsync(ImportTexture2DRawData request, CancellationToken cancellationToken) => Task.FromResult(
            new AssetData
            {
                Success = true,
                AssetURL = new Uri("resonite://texture", UriKind.Absolute),
            });

        public Task<AssetData> ImportTextureAsync(ImportTexture2DRawDataHDR request, CancellationToken cancellationToken) => Task.FromResult(
            new AssetData
            {
                Success = true,
                AssetURL = new Uri("resonite://texture", UriKind.Absolute),
            });

        public Task<Response> UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
        {
            UpdateComponentCallCount++;
            return new TaskCompletionSource<Response>(TaskCreationOptions.RunContinuationsAsynchronously).Task.WaitAsync(cancellationToken);
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
