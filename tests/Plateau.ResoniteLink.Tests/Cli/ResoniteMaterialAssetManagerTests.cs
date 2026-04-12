using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class ResoniteMaterialAssetManagerTests
{
    [Fact]
    public async Task CreateMaterialComponentAsyncDoesNotLetCallerCancellationPoisonSubsequentCreation()
    {
        TaskCompletionSource allowComponentCreation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int createComponentCallCount = 0;
        ResoniteMaterialAssetManager manager = new(
            static (_, _, _, _, _) => throw new NotSupportedException(),
            static (_, _, _, _, _) => throw new NotSupportedException(),
            static (_, _, _, _) => Task.FromResult(new ResoniteLinkSceneBuilder.CreatedSlot("material-slot-child", "Material")),
            async (_, _, componentType, _, cancellationToken) =>
            {
                int currentCall = Interlocked.Increment(ref createComponentCallCount);
                await allowComponentCreation.Task.WaitAsync(cancellationToken);
                return new ResoniteLinkSceneBuilder.CreatedComponent(
                    $"srv_component_{currentCall}",
                    componentType);
            },
            static (_, _, _, _) => Task.FromResult<Slot?>(null),
            static (_, _, _) => throw new NotSupportedException());
        using CancellationTokenSource cancellationTokenSource = new();

        using StubResoniteLinkClient firstClient = new();
        Task<CreatedMaterialAsset> canceledRequest = manager.CreateMaterialComponentAsync(
            firstClient,
            CreateMaterial(),
            new Dictionary<TextureReferenceKey, ResoniteTextureImport>(),
            "material-slot",
            null,
            "Material",
            "renderer-slot",
            "texture-slot",
            cancellationTokenSource.Token);

        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await canceledRequest);

        allowComponentCreation.TrySetResult();

        using StubResoniteLinkClient secondClient = new();
        CreatedMaterialAsset component = await manager.CreateMaterialComponentAsync(
            secondClient,
            CreateMaterial(),
            new Dictionary<TextureReferenceKey, ResoniteTextureImport>(),
            "material-slot",
            null,
            "Material",
            "renderer-slot",
            "texture-slot",
            CancellationToken.None);

        Assert.Equal("srv_component_2", component.MaterialComponentId);
        Assert.DoesNotContain("material-instance", component.MaterialComponentId, StringComparison.Ordinal);
        Assert.Equal(2, createComponentCallCount);
    }

    [Fact]
    public async Task CreateMaterialComponentAsyncDoesNotLetFaultedCreationPoisonRetry()
    {
        int createComponentCallCount = 0;
        ResoniteMaterialAssetManager manager = new(
            static (_, _, _, _, _) => throw new NotSupportedException(),
            static (_, _, _, _, _) => throw new NotSupportedException(),
            static (_, _, _, _) => Task.FromResult(new ResoniteLinkSceneBuilder.CreatedSlot("material-slot-child", "Material")),
            (_, _, componentType, _, _) =>
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
            static (_, _, _, _) => Task.FromResult<Slot?>(null),
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
                    "renderer-slot",
                    "texture-slot",
                    CancellationToken.None);
            });

        using StubResoniteLinkClient secondClient = new();
        CreatedMaterialAsset component = await manager.CreateMaterialComponentAsync(
            secondClient,
            CreateMaterial(),
            new Dictionary<TextureReferenceKey, ResoniteTextureImport>(),
            "material-slot",
            null,
            "Material",
            "renderer-slot",
            "texture-slot",
            CancellationToken.None);

        Assert.Equal("srv_component_2", component.MaterialComponentId);
        Assert.DoesNotContain("material-instance", component.MaterialComponentId, StringComparison.Ordinal);
        Assert.Equal(2, createComponentCallCount);
    }

    [Fact]
    public async Task CreateMaterialComponentAsyncCreatesSharedMaterialSlotOnlyAfterTextureImportSucceeds()
    {
        List<string> calls = [];
        TaskCompletionSource<Uri> allowImportCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ResoniteMaterialAssetManager manager = new(
            static (_, _, _, _, _) => throw new NotSupportedException(),
            static (_, _, _, _, _) => throw new NotSupportedException(),
            (_, _, _, _) =>
            {
                calls.Add("create-shared-slot");
                return Task.FromResult(new ResoniteLinkSceneBuilder.CreatedSlot("shared-slot", "Material"));
            },
            (_, _, componentType, _, _) =>
            {
                calls.Add($"create-component:{componentType}");
                return Task.FromResult(new ResoniteLinkSceneBuilder.CreatedComponent("material-component", componentType));
            },
            static (_, _, _, _) => Task.FromResult<Slot?>(null),
            async (_, _, ct) =>
            {
                calls.Add("import-texture");
                return await allowImportCompletion.Task.WaitAsync(ct);
            });
        using StubResoniteLinkClient client = new();
        Dictionary<TextureReferenceKey, ResoniteTextureImport> preparedTextures = new()
        {
            [ResoniteMaterialAssetManager.CreateTextureReferenceKey("textures/albedo.png", ResoniteTextureSourceKind.Dataset)] =
                ResoniteTextureImportFactory.CreateFromFile("/tmp/albedo.png"),
        };
        Task<CreatedMaterialAsset> pending = manager.CreateMaterialComponentAsync(
            client,
            CreateMaterial(
                texturePath: "textures/albedo.png",
                textureSourceKind: ResoniteTextureSourceKind.Dataset,
                baseColor: new ResoniteColor(0.8, 0.8, 0.8, 1.0)),
            preparedTextures,
            "scope-slot",
            "common-slot",
            "Material",
            "renderer-slot",
            "texture-slot",
            CancellationToken.None);

        await Task.Delay(50);
        Assert.Equal(["import-texture"], calls);

        allowImportCompletion.SetResult(new Uri("file:///tmp/albedo.png"));
        CreatedMaterialAsset created = await pending;

        Assert.Equal("material-component", created.MaterialComponentId);
        Assert.Equal(
            [
                "import-texture",
                "create-shared-slot",
                "create-component:[FrooxEngine]FrooxEngine.StaticTexture2D",
                "create-component:[FrooxEngine]FrooxEngine.PBS_Metallic",
            ],
            calls);
    }

    [Fact]
    public async Task CreateMaterialComponentAsyncDoesNotCreateSharedMaterialSlotWhenTextureImportFails()
    {
        List<string> calls = [];
        ResoniteMaterialAssetManager manager = new(
            static (_, _, _, _, _) => throw new NotSupportedException(),
            static (_, _, _, _, _) => throw new NotSupportedException(),
            (_, _, _, _) =>
            {
                calls.Add("create-shared-slot");
                return Task.FromResult(new ResoniteLinkSceneBuilder.CreatedSlot("shared-slot", "Material"));
            },
            (_, _, componentType, _, _) =>
            {
                calls.Add($"create-component:{componentType}");
                return Task.FromResult(new ResoniteLinkSceneBuilder.CreatedComponent("material-component", componentType));
            },
            static (_, _, _, _) => Task.FromResult<Slot?>(null),
            static (_, _, _) => throw new InvalidOperationException("texture import failed"));
        using StubResoniteLinkClient client = new();
        Dictionary<TextureReferenceKey, ResoniteTextureImport> preparedTextures = new()
        {
            [ResoniteMaterialAssetManager.CreateTextureReferenceKey("textures/albedo.png", ResoniteTextureSourceKind.Dataset)] =
                ResoniteTextureImportFactory.CreateFromFile("/tmp/albedo.png"),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.CreateMaterialComponentAsync(
            client,
            CreateMaterial(
                texturePath: "textures/albedo.png",
                textureSourceKind: ResoniteTextureSourceKind.Dataset,
                baseColor: new ResoniteColor(0.8, 0.8, 0.8, 1.0)),
            preparedTextures,
            "scope-slot",
            "common-slot",
            "Material",
            "renderer-slot",
            "texture-slot",
            CancellationToken.None));

        Assert.Equal([], calls);
    }

    [Fact]
    public async Task CreateMaterialComponentAsyncReusesExistingSharedMaterialBeforeTextureImport()
    {
        List<string> calls = [];
        Slot parentSlot = new()
        {
            ID = "common-slot",
            Children =
            [
                new Slot
                {
                    ID = "existing-material-slot",
                    Name = new Field_string
                    {
                        Value = "Material",
                    },
                    Components =
                    [
                        new Component
                        {
                            ID = "existing-material-component",
                            ComponentType = "[FrooxEngine]FrooxEngine.PBS_Metallic",
                        },
                    ],
                },
            ],
        };
        ResoniteMaterialAssetManager manager = new(
            static (_, _, _, _, _) => throw new NotSupportedException(),
            static (_, _, _, _, _) => throw new NotSupportedException(),
            (_, _, _, _) =>
            {
                calls.Add("create-shared-slot");
                return Task.FromResult(new ResoniteLinkSceneBuilder.CreatedSlot("shared-slot", "Material"));
            },
            (_, _, componentType, _, _) =>
            {
                calls.Add($"create-component:{componentType}");
                return Task.FromResult(new ResoniteLinkSceneBuilder.CreatedComponent("material-component", componentType));
            },
            (_, slotId, _, _) =>
            {
                calls.Add($"get-slot:{slotId}");
                return Task.FromResult<Slot?>(parentSlot);
            },
            static (_, _, _) => throw new InvalidOperationException("texture import should not run"));
        using StubResoniteLinkClient client = new();
        Dictionary<TextureReferenceKey, ResoniteTextureImport> preparedTextures = new()
        {
            [ResoniteMaterialAssetManager.CreateTextureReferenceKey("textures/albedo.png", ResoniteTextureSourceKind.Dataset)] =
                ResoniteTextureImportFactory.CreateFromFile("/tmp/albedo.png"),
        };

        CreatedMaterialAsset created = await manager.CreateMaterialComponentAsync(
            client,
            CreateMaterial(
                texturePath: "textures/albedo.png",
                textureSourceKind: ResoniteTextureSourceKind.Dataset,
                baseColor: new ResoniteColor(0.8, 0.8, 0.8, 1.0)),
            preparedTextures,
            "scope-slot",
            "common-slot",
            "Material",
            "renderer-slot",
            "texture-slot",
            CancellationToken.None);

        Assert.Equal("existing-material-component", created.MaterialComponentId);
        Assert.Equal(["get-slot:common-slot"], calls);
    }

    [Fact]
    public async Task CreateMaterialComponentAsyncImportsBundledAlbedoWithoutPreparedTextureMap()
    {
        List<string> calls = [];
        ResoniteMaterialAssetManager manager = new(
            static (_, _, _, _, _) => throw new NotSupportedException(),
            static (_, _, _, _, _) => throw new NotSupportedException(),
            static (_, _, _, _) => Task.FromResult(new ResoniteLinkSceneBuilder.CreatedSlot("shared-slot", "Material")),
            (_, _, componentType, _, _) =>
            {
                calls.Add($"create-component:{componentType}");
                return Task.FromResult(new ResoniteLinkSceneBuilder.CreatedComponent("material-component", componentType));
            },
            static (_, _, _, _) => Task.FromResult<Slot?>(null),
            (_, textureImport, _) =>
            {
                calls.Add(textureImport switch
                {
                    ResoniteFileTextureImport fileImport => $"import-file:{Path.GetFileName(fileImport.AbsolutePath)}",
                    _ => $"import:{textureImport.GetType().Name}",
                });

                return Task.FromResult(new Uri("file:///tmp/bundled.png"));
            });
        using StubResoniteLinkClient client = new();

        await manager.CreateMaterialComponentAsync(
            client,
            CreateMaterial(
                texturePath: "default-materials/roof/Concrete033_2K-JPG_Color.jpg",
                textureSourceKind: ResoniteTextureSourceKind.Bundled),
            new Dictionary<TextureReferenceKey, ResoniteTextureImport>(),
            "scope-slot",
            "common-slot",
            "Material",
            "renderer-slot",
            "texture-slot",
            CancellationToken.None);

        Assert.Contains(calls, static call => call.StartsWith("import-file:Concrete033_2K-JPG_Color", StringComparison.Ordinal));
        Assert.Contains(calls, static call => call == "create-component:[FrooxEngine]FrooxEngine.PBS_Metallic");
    }

    private static ResoniteMaterialBinding CreateMaterial(
        string? texturePath = null,
        ResoniteTextureSourceKind textureSourceKind = ResoniteTextureSourceKind.Bundled,
        ResoniteColor? baseColor = null)
    {
        return new ResoniteMaterialBinding(
            MaterialKey: "test-material",
            BaseColor: baseColor ?? new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Standard,
            TexturePath: texturePath,
            TextureSourceKind: textureSourceKind,
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

        public Task<BatchResponse> RunDataModelOperationBatchAsync(IReadOnlyList<DataModelOperation> operations, CancellationToken cancellationToken) =>
            Task.FromResult(new BatchResponse
            {
                Success = true,
                Responses = [],
            });

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
