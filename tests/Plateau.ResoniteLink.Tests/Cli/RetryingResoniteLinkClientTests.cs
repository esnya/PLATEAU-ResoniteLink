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
        ResoniteLinkOperationException exception = await Assert.ThrowsAsync<ResoniteLinkOperationException>(
            () => client.ImportMeshAsync(
                new ImportMeshRawData
                {
                    RawBinaryPayload = [1, 2, 3],
                    VertexCount = 3,
                },
                CancellationToken.None));
        Assert.Equal("ImportMesh", exception.OperationName);

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
    public async Task MutationOperationsWaitForImportMeshToComplete()
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
    public async Task ImportMeshAsyncIgnoresConfiguredDiagnosticTimeout()
    {
        using BlockingReconnectableClient innerClient = new();
        using RetryingResoniteLinkClient client = new(
            () => innerClient,
            reporter: null,
            importMeshTimeoutMilliseconds: 100);

        await client.ConnectAsync(new Uri("ws://localhost:12345/"), CancellationToken.None);
        using CancellationTokenSource cancellation = new();

        Task<Uri> importTask = client.ImportMeshAsync(
            new ImportMeshRawData
            {
                RawBinaryPayload = [1, 2, 3],
                VertexCount = 3,
            },
            cancellation.Token);

        await innerClient.ImportMeshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(150);

        Assert.False(importTask.IsCompleted);

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => importTask);
        Assert.Equal(1, innerClient.ImportMeshCallCount);
    }

    [Fact]
    public async Task ImportMeshAsyncDoesNotApplyTimeoutWhenDisabledByDefault()
    {
        using BlockingReconnectableClient innerClient = new();
        using RetryingResoniteLinkClient client = new(() => innerClient);
        using CancellationTokenSource cancellation = new();

        await client.ConnectAsync(new Uri("ws://localhost:12345/"), CancellationToken.None);

        Task<Uri> importTask = client.ImportMeshAsync(
            new ImportMeshRawData
            {
                RawBinaryPayload = [1, 2, 3],
                VertexCount = 3,
            },
            cancellation.Token);

        await innerClient.ImportMeshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(150);

        Assert.False(importTask.IsCompleted);

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => importTask);
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

    [Fact]
    public async Task GetSlotAsyncWaitsForActiveImportBeforeRetryingReconnect()
    {
        int createdClientCount = 0;
        using CoordinatedReconnectableClient firstClient = new(failGetSlot: true);
        using CoordinatedReconnectableClient secondClient = new();

        using RetryingResoniteLinkClient client = new(
            () =>
            {
                createdClientCount++;
                return createdClientCount == 1 ? firstClient : secondClient;
            });

        await client.ConnectAsync(new Uri("ws://localhost:12345/"), CancellationToken.None);

        Task<Uri> importTask = client.ImportMeshAsync(
            new ImportMeshRawData
            {
                RawBinaryPayload = [1, 2, 3],
                VertexCount = 3,
            },
            CancellationToken.None);
        await firstClient.ImportMeshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task<Slot?> getSlotTask = client.GetSlotAsync("slot-id", 0, CancellationToken.None);

        await Task.Delay(100);
        Assert.False(getSlotTask.IsCompleted);

        firstClient.AllowImportMeshCompletion.TrySetResult();
        await importTask;
        Slot? slot = await getSlotTask;

        Assert.NotNull(slot);
        Assert.Equal("slot-id", slot!.ID);
        Assert.Equal(1, firstClient.DisposeCallCount);
        Assert.Equal(1, secondClient.ConnectCallCount);
    }

    [Fact]
    public async Task CancelingQueuedGetSlotDoesNotBlockLaterOperations()
    {
        using CoordinatedReconnectableClient firstClient = new();
        using RetryingResoniteLinkClient client = new(() => firstClient);

        await client.ConnectAsync(new Uri("ws://localhost:12345/"), CancellationToken.None);

        Task<Uri> importTask = client.ImportMeshAsync(
            new ImportMeshRawData
            {
                RawBinaryPayload = [1, 2, 3],
                VertexCount = 3,
            },
            CancellationToken.None);
        await firstClient.ImportMeshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using CancellationTokenSource getSlotCancellation = new();
        Task<Slot?> getSlotTask = client.GetSlotAsync("slot-id", 0, getSlotCancellation.Token);

        await Task.Delay(100);
        Assert.False(getSlotTask.IsCompleted);
        await getSlotCancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => getSlotTask);

        firstClient.AllowImportMeshCompletion.TrySetResult();
        await importTask;

        string slotId = await client.AddSlotAsync(
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

        Assert.Equal("srv_slot_1", slotId);
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

    private sealed class CoordinatedReconnectableClient : IResoniteLinkClient
    {
        private readonly bool failGetSlot;
        private bool disposed;
        private int nextSlotId;

        public TaskCompletionSource ImportMeshStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowImportMeshCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ConnectCallCount { get; private set; }

        public int DisposeCallCount { get; private set; }

        public CoordinatedReconnectableClient(bool failGetSlot = false)
        {
            this.failGetSlot = failGetSlot;
        }

        public void Dispose()
        {
            disposed = true;
            DisposeCallCount++;
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
            return Task.FromResult("srv_component_1");
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
            if (failGetSlot)
            {
                throw new InvalidOperationException("Simulated get slot failure.");
            }

            return Task.FromResult<Slot?>(new Slot
            {
                ID = slotId,
            });
        }

        public async Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImportMeshStarted.TrySetResult();
            await AllowImportMeshCompletion.Task.WaitAsync(cancellationToken);
            ObjectDisposedException.ThrowIf(disposed, this);

            return new Uri("resdb:///mesh/ok", UriKind.Absolute);
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
