using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class ResoniteMaterialAssetManagerTests
{
    [Fact]
    public async Task CreateMaterialComponentAsyncDoesNotLetCallerCancellationPoisonSharedCreation()
    {
        TaskCompletionSource allowComponentCreation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int createComponentCallCount = 0;
        ResoniteMaterialAssetManager manager = new(
            static (_, _, _, _) => throw new NotSupportedException(),
            static (_, _, _, _) => throw new NotSupportedException(),
            static (_, _, _) => throw new NotSupportedException(),
            async (_, componentType, _, cancellationToken) =>
            {
                int currentCall = Interlocked.Increment(ref createComponentCallCount);
                await allowComponentCreation.Task.WaitAsync(cancellationToken);
                return new ResoniteLinkSceneBuilder.CreatedComponent(
                    $"srv_component_{currentCall}",
                    componentType);
            },
            static (_, _, _) => throw new NotSupportedException());
        using CancellationTokenSource cancellationTokenSource = new();

        using StubResoniteLinkClient firstClient = new();
        Task<ResoniteLinkSceneBuilder.CreatedComponent> canceledRequest = manager.CreateMaterialComponentAsync(
            firstClient,
            CreateMaterial(),
            new Dictionary<TextureReferenceKey, ResoniteTextureImport>(),
            "material-slot",
            null,
            "Material",
            cancellationTokenSource.Token);

        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await canceledRequest);

        allowComponentCreation.TrySetResult();

        using StubResoniteLinkClient secondClient = new();
        ResoniteLinkSceneBuilder.CreatedComponent component = await manager.CreateMaterialComponentAsync(
            secondClient,
            CreateMaterial(),
            new Dictionary<TextureReferenceKey, ResoniteTextureImport>(),
            "material-slot",
            null,
            "Material",
            CancellationToken.None);

        Assert.Equal("srv_component_1", component.ComponentId);
        Assert.DoesNotContain("material-instance", component.ComponentId, StringComparison.Ordinal);
        Assert.Equal(1, createComponentCallCount);
    }

    [Fact]
    public async Task CreateMaterialComponentAsyncRemovesFaultedSharedCreationAndRetries()
    {
        int createComponentCallCount = 0;
        ResoniteMaterialAssetManager manager = new(
            static (_, _, _, _) => throw new NotSupportedException(),
            static (_, _, _, _) => throw new NotSupportedException(),
            static (_, _, _) => throw new NotSupportedException(),
            (_, componentType, _, _) =>
            {
                int currentCall = Interlocked.Increment(ref createComponentCallCount);
                if (currentCall == 1)
                {
                    throw new InvalidOperationException("Simulated component creation failure.");
                }

                return Task.FromResult(new ResoniteLinkSceneBuilder.CreatedComponent(
                    $"srv_component_{currentCall}",
                    componentType));
            },
            static (_, _, _) => throw new NotSupportedException());

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
            {
                using StubResoniteLinkClient firstClient = new();
                await manager.CreateMaterialComponentAsync(
                    firstClient,
                    CreateMaterial(),
                    new Dictionary<TextureReferenceKey, ResoniteTextureImport>(),
                    "material-slot",
                    null,
                    "Material",
                    CancellationToken.None);
            });

        using StubResoniteLinkClient secondClient = new();
        ResoniteLinkSceneBuilder.CreatedComponent component = await manager.CreateMaterialComponentAsync(
            secondClient,
            CreateMaterial(),
            new Dictionary<TextureReferenceKey, ResoniteTextureImport>(),
            "material-slot",
            null,
            "Material",
            CancellationToken.None);

        Assert.Equal("srv_component_2", component.ComponentId);
        Assert.DoesNotContain("material-instance", component.ComponentId, StringComparison.Ordinal);
        Assert.Equal(2, createComponentCallCount);
    }

    private static ResoniteMaterialBinding CreateMaterial()
    {
        return new ResoniteMaterialBinding(
            MaterialKey: "test-material",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePath: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0]);
    }

    private sealed class StubResoniteLinkClient : IResoniteLinkClient
    {
        public void Dispose()
        {
        }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string> AddComponentAsync(AddComponent request, CancellationToken cancellationToken) => Task.FromResult("srv_component");

        public Task<string> AddSlotAsync(AddSlot request, CancellationToken cancellationToken) => Task.FromResult("srv_slot");

        public Task RunDataModelOperationBatchAsync(IReadOnlyList<DataModelOperation> operations, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
        {
            return Task.FromResult<Component?>(new Component
            {
                ID = componentId,
            });
        }

        public Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken) => Task.FromResult<Slot?>(null);

        public Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
