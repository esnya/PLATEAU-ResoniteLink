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
    public async Task ImportMeshAsyncRetiresClientAfterFatalProtocolFailure()
    {
        int createdClientCount = 0;
        List<string> progressMessages = [];
        using StubReconnectableClient firstClient = new(importMeshExceptionMessage: "ResoniteLink import mesh returned a null response.");
        using StubReconnectableClient secondClient = new();

        using RetryingResoniteLinkClient client = new(
            () =>
            {
                createdClientCount++;
                return createdClientCount == 1 ? firstClient : secondClient;
            },
            progressMessages.Add);

        await client.ConnectAsync(new Uri("ws://localhost:12345/"), CancellationToken.None);

        await Assert.ThrowsAsync<ResoniteLinkOperationException>(
            () => client.ImportMeshAsync(
                new ImportMeshRawData
                {
                    RawBinaryPayload = [1, 2, 3],
                    VertexCount = 3,
                },
                CancellationToken.None));

        string createdSlotId = await ((IResoniteLinkClient)client).AddSlotAsync(
            new AddSlot
            {
                Data = new Slot
                {
                    Parent = new Reference
                    {
                        TargetID = "parent-id",
                    },
                    Name = new Field_string
                    {
                        Value = "Recovered",
                    },
                },
            },
            CancellationToken.None);

        Assert.Equal("srv_slot_1", createdSlotId);
        Assert.True(firstClient.IsDisposed);
        Assert.Equal(1, firstClient.ImportMeshCallCount);
        Assert.Equal(2, createdClientCount);
        Assert.Equal(0, secondClient.ConnectCallCount);
        Assert.Contains(
            progressMessages,
            static message => message.Contains("retired the active client", StringComparison.Ordinal));
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
    public async Task MutationOperationsDoNotWaitForImportMeshToComplete()
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

        Task addSlotTask = ((IResoniteLinkClient)client).AddSlotAsync(
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

        await addSlotTask.WaitAsync(TimeSpan.FromSeconds(5));

        innerClient.AllowImportMeshCompletion.SetResult();

        await importTask;

        Assert.Equal(1, innerClient.ImportMeshCallCount);
        Assert.Equal(1, innerClient.AddSlotCallCount);
        Assert.False(innerClient.AddSlotStartedAfterImportCompleted);
    }

    [Fact]
    public async Task AddSlotAsyncReturnsCreatedIdFromInnerClient()
    {
        using StubReconnectableClient innerClient = new();
        using RetryingResoniteLinkClient client = new(() => innerClient);

        await client.ConnectAsync(new Uri("ws://localhost:12345/"), CancellationToken.None);
        string createdSlotId = await ((IResoniteLinkClient)client).AddSlotAsync(
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
    public async Task GetSlotAsyncReconnectsWhileImportIsInFlightWithoutDisposingTheActiveClient()
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

        Slot? slot = await getSlotTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(slot);
        Assert.Equal("slot-id", slot!.ID);
        Assert.Equal(0, firstClient.DisposeCallCount);

        firstClient.AllowImportMeshCompletion.TrySetResult();
        await importTask;
        Assert.Equal(1, firstClient.DisposeCallCount);
        Assert.Equal(1, secondClient.ConnectCallCount);
    }

    [Fact]
    public async Task FailedReconnectConnectLeavesExistingClientUsable()
    {
        int createdClientCount = 0;
        using StubReconnectableClient firstClient = new(failGetSlot: true);
        using StubReconnectableClient secondClient = new(failConnect: true);

        using RetryingResoniteLinkClient client = new(
            () =>
            {
                createdClientCount++;
                return createdClientCount == 1 ? firstClient : secondClient;
            });

        await client.ConnectAsync(new Uri("ws://localhost:12345/"), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetSlotAsync("slot-id", 0, CancellationToken.None));

        string createdSlotId = await ((IResoniteLinkClient)client).AddSlotAsync(
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
        Assert.False(firstClient.IsDisposed);
        Assert.Equal(1, firstClient.ConnectCallCount);
        Assert.Equal(1, secondClient.ConnectCallCount);
    }

    private sealed class LongHeldReconnectableClient : IResoniteLinkClient
    {
        private readonly bool blockAddComponent;
        private readonly bool blockAddSlot;
        private readonly bool blockBatch;
        private readonly bool blockImportMesh;
        private int nextComponentId;
        private int nextSlotId;

        public LongHeldReconnectableClient(
            bool blockImportMesh = false,
            bool blockAddSlot = false,
            bool blockAddComponent = false,
            bool blockBatch = false)
        {
            this.blockImportMesh = blockImportMesh;
            this.blockAddSlot = blockAddSlot;
            this.blockAddComponent = blockAddComponent;
            this.blockBatch = blockBatch;
        }

        public TaskCompletionSource AddComponentStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowAddComponentCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AddSlotStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowAddSlotCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ImportMeshStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowImportMeshCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource RunDataModelOperationBatchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowRunDataModelOperationBatchCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose()
        {
        }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public async Task<string> AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddComponentStarted.TrySetResult();
            if (blockAddComponent)
            {
                await AllowAddComponentCompletion.Task.WaitAsync(cancellationToken);
            }

            return $"srv_component_{Interlocked.Increment(ref nextComponentId)}";
        }

        public async Task<string> AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddSlotStarted.TrySetResult();
            if (blockAddSlot)
            {
                await AllowAddSlotCompletion.Task.WaitAsync(cancellationToken);
            }

            return $"srv_slot_{Interlocked.Increment(ref nextSlotId)}";
        }

        public async Task<BatchResponse> RunDataModelOperationBatchAsync(
            IReadOnlyList<DataModelOperation> operations,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RunDataModelOperationBatchStarted.TrySetResult();
            if (blockBatch)
            {
                await AllowRunDataModelOperationBatchCompletion.Task.WaitAsync(cancellationToken);
            }

            return new BatchResponse
            {
                Success = true,
                Responses = [],
            };
        }

        public Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Component?>(null);
        }

        public Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Slot?>(new Slot
            {
                ID = slotId,
            });
        }

        public async Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImportMeshStarted.TrySetResult();
            if (blockImportMesh)
            {
                await AllowImportMeshCompletion.Task.WaitAsync(cancellationToken);
            }

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

    private sealed class StubReconnectableClient(
        bool failImportMesh = false,
        bool failGetSlot = false,
        bool failConnect = false,
        string? importMeshExceptionMessage = null) : IResoniteLinkClient
    {
        private int nextComponentId;
        private int nextSlotId;

        public int ConnectCallCount { get; private set; }

        public int ImportMeshCallCount { get; private set; }

        public int GetSlotCallCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectCallCount++;
            if (failConnect)
            {
                throw new InvalidOperationException("Simulated connect failure.");
            }

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
            if (failImportMesh || importMeshExceptionMessage is not null)
            {
                throw new InvalidOperationException(importMeshExceptionMessage ?? "Simulated mesh import failure.");
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

    private sealed class NeverCompletingReconnectableTransport : IResoniteLinkTransport
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<NewEntityId> AddComponentAsync(AddComponent request) => throw new NotSupportedException();

        public Task<NewEntityId> AddSlotAsync(AddSlot request) =>
            new TaskCompletionSource<NewEntityId>(TaskCreationOptions.RunContinuationsAsynchronously).Task;

        public Task<BatchResponse> RunDataModelOperationBatchAsync(List<DataModelOperation> operations) => throw new NotSupportedException();

        public Task<ComponentData> GetComponentDataAsync(GetComponent request) => throw new NotSupportedException();

        public Task<SlotData> GetSlotDataAsync(GetSlot request) => throw new NotSupportedException();

        public Task<AssetData> ImportMeshAsync(ImportMeshRawData request) => throw new NotSupportedException();

        public Task<AssetData> ImportTextureFileAsync(ImportTexture2DFile request) => throw new NotSupportedException();

        public Task<AssetData> ImportTextureRawAsync(ImportTexture2DRawData request) => throw new NotSupportedException();

        public Task<AssetData> ImportTextureRawHdrAsync(ImportTexture2DRawDataHDR request) => throw new NotSupportedException();

        public Task<Response> UpdateComponentAsync(UpdateComponent request) => throw new NotSupportedException();
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
