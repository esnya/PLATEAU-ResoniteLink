using Plateau.ResoniteLink.Cli;

using ResoniteLink;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class RetryingResoniteLinkClientTests
{
    [Fact]
    public async Task ImportMeshAsyncDoesNotReconnectOrRetryAfterFailure()
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
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ImportMeshAsync(
                new ImportMeshRawData
                {
                    RawBinaryPayload = [1, 2, 3],
                    VertexCount = 3,
                },
                CancellationToken.None));

        Assert.Equal(1, firstClient.ImportMeshCallCount);
        Assert.Equal(0, secondClient.ImportMeshCallCount);
        Assert.Equal(1, firstClient.ConnectCallCount);
        Assert.Equal(0, secondClient.ConnectCallCount);
        Assert.Equal(1, createdClientCount);
        Assert.Contains(
            progressMessages,
            static message => message.Contains("failed without retry", StringComparison.Ordinal));
        Assert.DoesNotContain(
            progressMessages,
            static message => message.Contains("Reconnecting before retry", StringComparison.Ordinal));
        Assert.DoesNotContain(
            progressMessages,
            static message => message.Contains("Reconnected ResoniteLink client", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetSlotAsyncReconnectsAndRetriesAfterFailure()
    {
        int createdClientCount = 0;
        List<string> progressMessages = [];
        using StubReconnectableClient firstClient = new(failGetSlot: true);
        using StubReconnectableClient secondClient = new();

        using RetryingResoniteLinkClient client = new(
            () =>
            {
                createdClientCount++;
                return createdClientCount == 1 ? firstClient : secondClient;
            },
            progressMessages.Add);

        await client.ConnectAsync(new Uri("ws://localhost:12345/"), CancellationToken.None);
        Slot? result = await client.GetSlotAsync("slot-id", 0, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("slot-id", result!.ID);
        Assert.Equal(1, firstClient.GetSlotCallCount);
        Assert.Equal(1, secondClient.GetSlotCallCount);
        Assert.Equal(1, firstClient.ConnectCallCount);
        Assert.Equal(1, secondClient.ConnectCallCount);
        Assert.Equal(2, createdClientCount);
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

    [Fact]
    public async Task ImportMeshAsyncTimesOutWhenInnerClientStopsResponding()
    {
        List<string> progressMessages = [];
        using BlockingReconnectableClient innerClient = new();
        using RetryingResoniteLinkClient client = new(
            () => innerClient,
            progressMessages.Add,
            importMeshTimeoutMilliseconds: 100);

        await client.ConnectAsync(new Uri("ws://localhost:12345/"), CancellationToken.None);

        Task<Uri> importTask = client.ImportMeshAsync(
            new ImportMeshRawData
            {
                RawBinaryPayload = [1, 2, 3],
                VertexCount = 3,
            },
            CancellationToken.None);

        await innerClient.ImportMeshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(() => importTask);

        Assert.Contains("did not complete within 100ms", exception.Message, StringComparison.Ordinal);
        Assert.Contains("pending_responses=2", exception.Message, StringComparison.Ordinal);
        Assert.Contains("failure_exception=InvalidOperationException", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            progressMessages,
            static message => message.Contains("ImportMesh failed without retry", StringComparison.Ordinal));
        Assert.Contains(
            progressMessages,
            static message => message.Contains("[live][diagnostic] ImportMesh timeout", StringComparison.Ordinal));
        Assert.Equal(1, innerClient.ImportMeshCallCount);
    }

    [Fact]
    public async Task AddSlotAsyncReturnsCreatedIdFromInnerClient()
    {
        using StubReconnectableClient innerClient = new();
        using RetryingResoniteLinkClient client = new(() => innerClient);

        await client.ConnectAsync(new Uri("ws://localhost:12345/"), CancellationToken.None);
        string createdSlotId = await client.AddSlotAsync(
            new AddSlot
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
            },
            CancellationToken.None);

        Assert.Equal("srv_slot_1", createdSlotId);
    }

    private sealed class StubReconnectableClient(bool failImportMesh = false, bool failGetSlot = false) : IResoniteLinkClient
    {
        private int nextComponentId;
        private int nextSlotId;

        public int ConnectCallCount { get; private set; }

        public int ImportMeshCallCount { get; private set; }

        public int GetSlotCallCount { get; private set; }

        public void Dispose()
        {
        }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectCallCount++;
            return Task.CompletedTask;
        }

        public Task<string> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult($"srv_component_{Interlocked.Increment(ref nextComponentId)}");
        }

        public Task<string> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult($"srv_slot_{Interlocked.Increment(ref nextSlotId)}");
        }

        public Task<BatchResponse> RunDataModelOperationBatchAsync(
            IReadOnlyList<DataModelOperation> operations,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new BatchResponse
            {
                Success = true,
                Responses = [],
            });
        }

        public Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Component?>(null);
        }

        public Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetSlotCallCount++;
            if (failGetSlot)
            {
                throw new InvalidOperationException("Simulated get slot failure.");
            }

            return Task.FromResult<Slot?>(new Slot
            {
                ID = slotId,
            });
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
        private readonly FakeLinkDiagnostics link = new();
        private int nextComponentId;
        private int nextSlotId;

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

        public Task<string> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult($"srv_component_{Interlocked.Increment(ref nextComponentId)}");
        }

        public Task<string> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddSlotCallCount++;
            AddSlotStartedAfterImportCompleted = AllowImportMeshCompletion.Task.IsCompleted;
            return Task.FromResult($"srv_slot_{Interlocked.Increment(ref nextSlotId)}");
        }

        public Task<BatchResponse> RunDataModelOperationBatchAsync(
            IReadOnlyList<DataModelOperation> operations,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new BatchResponse
            {
                Success = true,
                Responses = [],
            });
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

        private sealed class FakeLinkDiagnostics
        {
            private readonly FakeWebSocket _client = new();
            private readonly Dictionary<string, string> _pendingResponses = new()
            {
                ["req-1"] = "pending",
                ["req-2"] = "pending",
            };

            public Exception FailureException { get; } = new InvalidOperationException("receiver failed");

            public bool IsConnected { get; } = true;
        }

        private sealed class FakeWebSocket
        {
            public string State { get; } = "Open";
        }
    }
}
