using Plateau.ResoniteLink.Cli;

using ResoniteLink;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class RetryingResoniteLinkClientTests
{
    [Fact]
    public async Task ImportMeshAsyncReconnectsAndRetriesAfterFailure()
    {
        int createdClientCount = 0;
        List<string> progressMessages = [];
        using StubReconnectableClient firstClient = new(failImportMesh: true);
        using StubReconnectableClient secondClient = new(failImportMesh: false);

        using RetryingResoniteLinkClient client = new(
            () =>
            {
                createdClientCount++;
                return createdClientCount == 1 ? firstClient : secondClient;
            },
            progressMessages.Add);

        await client.ConnectAsync(new Uri("ws://localhost:12345/"), CancellationToken.None);
        Uri result = await client.ImportMeshAsync(
            new ImportMeshRawData
            {
                RawBinaryPayload = [1, 2, 3],
                VertexCount = 3,
            },
            CancellationToken.None);

        Assert.Equal(new Uri("resdb:///mesh/ok", UriKind.Absolute), result);
        Assert.Equal(1, firstClient.ImportMeshCallCount);
        Assert.Equal(1, secondClient.ImportMeshCallCount);
        Assert.Equal(1, firstClient.ConnectCallCount);
        Assert.Equal(1, secondClient.ConnectCallCount);
        Assert.Contains(
            progressMessages,
            static message => message.Contains("Reconnecting before retry", StringComparison.Ordinal));
        Assert.Contains(
            progressMessages,
            static message => message.Contains("Reconnected ResoniteLink client", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConcurrentOperationsAreSerializedPerClient()
    {
        using BlockingReconnectableClient innerClient = new();
        using RetryingResoniteLinkClient client = new(() => innerClient);

        await client.ConnectAsync(new Uri("ws://localhost:12345/"), CancellationToken.None);

        Task<Uri> importTask = client.ImportMeshAsync(
            new ImportMeshRawData
            {
                RawBinaryPayload = [1, 2, 3],
                VertexCount = 3,
            },
            CancellationToken.None);

        await innerClient.ImportMeshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task addSlotTask = client.AddSlotAsync(
            new AddSlot
            {
                Data = new Slot
                {
                    ID = "slot-id",
                    Parent = new Reference
                    {
                        TargetID = "parent-id",
                    },
                    Name = new Field_string
                    {
                        Value = "Slot",
                    },
                },
            },
            CancellationToken.None);

        await Task.Delay(100);
        Assert.False(addSlotTask.IsCompleted);

        innerClient.AllowImportMeshCompletion.SetResult();

        await importTask;
        await addSlotTask;

        Assert.Equal(1, innerClient.ImportMeshCallCount);
        Assert.Equal(1, innerClient.AddSlotCallCount);
        Assert.True(innerClient.AddSlotStartedAfterImportCompleted);
    }

    private sealed class StubReconnectableClient(bool failImportMesh) : IResoniteLinkClient
    {
        public int ConnectCallCount { get; private set; }

        public int ImportMeshCallCount { get; private set; }

        public void Dispose()
        {
        }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectCallCount++;
            return Task.CompletedTask;
        }

        public Task AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
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
            return Task.FromResult<Component?>(null);
        }

        public Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Slot?>(null);
        }

        public Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImportMeshCallCount++;
            if (failImportMesh)
            {
                throw new InvalidOperationException("Simulated mesh import failure.");
            }

            return Task.FromResult(new Uri("resdb:///mesh/ok", UriKind.Absolute));
        }

        public Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new Uri("resdb:///texture/ok", UriKind.Absolute));
        }

        public Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingReconnectableClient : IResoniteLinkClient
    {
        public TaskCompletionSource ImportMeshStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowImportMeshCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ConnectCallCount { get; private set; }

        public int ImportMeshCallCount { get; private set; }

        public int AddSlotCallCount { get; private set; }

        public bool AddSlotStartedAfterImportCompleted { get; private set; }

        public void Dispose()
        {
        }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectCallCount++;
            return Task.CompletedTask;
        }

        public Task AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddSlotCallCount++;
            AddSlotStartedAfterImportCompleted = AllowImportMeshCompletion.Task.IsCompleted;
            return Task.CompletedTask;
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
            return Task.FromResult<Component?>(null);
        }

        public Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Slot?>(null);
        }

        public async Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImportMeshCallCount++;
            ImportMeshStarted.TrySetResult();
            await AllowImportMeshCompletion.Task.WaitAsync(cancellationToken);
            return new Uri("resdb:///mesh/serialized", UriKind.Absolute);
        }

        public Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new Uri("resdb:///texture/ok", UriKind.Absolute));
        }

        public Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
