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
            "property-block-scope",
            false,
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
            "property-block-scope",
            false,
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
                    "property-block-scope",
                    false,
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
            "property-block-scope",
            false,
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
            "property-block-scope",
            false,
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
            "property-block-scope",
            false,
            CancellationToken.None));

        Assert.Equal([], calls);
    }

    [Fact]
    public async Task CreateMaterialComponentAsyncCreatesSharedAlbedoPropertyBlockWhenRequested()
    {
        List<string> createdComponentTypes = [];
        ResoniteMaterialAssetManager manager = new(
            static (_, _, _, _, _) => throw new NotSupportedException(),
            (_, _, componentType, _, _) => Task.FromResult(
                new ResoniteLinkSceneBuilder.CreatedComponent(
                    componentType == "[FrooxEngine]FrooxEngine.StaticTexture2D"
                        ? "albedo-texture-component"
                        : throw new InvalidOperationException("Unexpected dedicated component type."),
                    componentType)),
            static (_, _, _, _) => Task.FromResult(new ResoniteLinkSceneBuilder.CreatedSlot("shared-slot", "Material")),
            (_, _, componentType, members, _) =>
            {
                createdComponentTypes.Add(componentType);
                if (componentType == "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock")
                {
                    Reference textureReference = Assert.IsType<Reference>(members["Texture"]);
                    Assert.Equal("albedo-texture-component", textureReference.TargetID);
                    return Task.FromResult(new ResoniteLinkSceneBuilder.CreatedComponent("property-block-component", componentType));
                }

                return Task.FromResult(new ResoniteLinkSceneBuilder.CreatedComponent("material-component", componentType));
            },
            static (_, _, _, _) => Task.FromResult<Slot?>(null),
            static (_, _, _) => Task.FromResult(new Uri("resdb:///texture/shared-albedo", UriKind.Absolute)));
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
            null,
            "Material",
            "property-block-scope",
            true,
            CancellationToken.None);

        Assert.Equal("material-component", created.MaterialComponentId);
        Assert.Equal("property-block-component", created.MaterialPropertyBlockComponentId);
        Assert.Contains("[FrooxEngine]FrooxEngine.PBS_Metallic", createdComponentTypes);
        Assert.Contains("[FrooxEngine]FrooxEngine.MainTexturePropertyBlock", createdComponentTypes);
    }

    [Fact]
    public async Task CreateMaterialComponentAsyncReusesExistingSharedMaterialBeforeImportingTextures()
    {
        int importTextureCallCount = 0;
        ResoniteMaterialAssetManager manager = new(
            static (_, _, _, _, _) => throw new InvalidOperationException("Shared asset creation should not run."),
            static (_, _, _, _, _) => throw new InvalidOperationException("Dedicated asset creation should not run."),
            static (_, _, _, _) => throw new InvalidOperationException("Shared slot creation should not run."),
            static (_, _, _, _, _) => throw new InvalidOperationException("Component creation should not run."),
            static (_, slotId, _, _) => Task.FromResult<Slot?>(slotId switch
            {
                "common-parent" => new Slot
                {
                    ID = "common-parent",
                    Children =
                    [
                        new Slot
                        {
                            ID = "existing-material-slot",
                            Name = new Field_string
                            {
                                Value = "Material",
                            },
                        },
                    ],
                },
                "existing-material-slot" => new Slot
                {
                    ID = "existing-material-slot",
                    Components =
                    [
                        new Component
                        {
                            ID = "existing-material-component",
                            ComponentType = "[FrooxEngine]FrooxEngine.PBS_Metallic",
                            Members = new Dictionary<string, Member>(StringComparer.Ordinal),
                        },
                    ],
                },
                _ => null,
            }),
            (_, _, _) =>
            {
                importTextureCallCount++;
                return Task.FromResult(new Uri("resdb:///texture/should-not-import", UriKind.Absolute));
            });
        using StubResoniteLinkClient client = new();

        CreatedMaterialAsset created = await manager.CreateMaterialComponentAsync(
            client,
            CreateMaterial(
                texturePath: "textures/albedo.png",
                textureSourceKind: ResoniteTextureSourceKind.Dataset,
                baseColor: new ResoniteColor(0.8, 0.8, 0.8, 1.0)),
            new Dictionary<TextureReferenceKey, ResoniteTextureImport>
            {
                [ResoniteMaterialAssetManager.CreateTextureReferenceKey("textures/albedo.png", ResoniteTextureSourceKind.Dataset)] =
                    ResoniteTextureImportFactory.CreateFromFile("/tmp/albedo.png"),
            },
            "scope-slot",
            "common-parent",
            "Material",
            "property-block-scope",
            false,
            CancellationToken.None);

        Assert.Equal("existing-material-component", created.MaterialComponentId);
        Assert.Null(created.MaterialPropertyBlockComponentId);
        Assert.Equal(0, importTextureCallCount);
    }

    [Fact]
    public async Task CreateMaterialComponentAsyncReusesSharedAlbedoPropertyBlockForSameTexture()
    {
        List<string> createdComponentTypes = [];
        int sharedSlotCreationCount = 0;
        ResoniteMaterialAssetManager manager = new(
            static (_, _, _, _, _) => throw new NotSupportedException(),
            (_, _, componentType, _, _) =>
            {
                createdComponentTypes.Add(componentType);
                return Task.FromResult(new ResoniteLinkSceneBuilder.CreatedComponent(
                    componentType == "[FrooxEngine]FrooxEngine.StaticTexture2D"
                        ? "shared-albedo-texture-component"
                        : throw new InvalidOperationException("Unexpected dedicated component type."),
                    componentType));
            },
            (_, _, _, _) =>
            {
                sharedSlotCreationCount++;
                return Task.FromResult(new ResoniteLinkSceneBuilder.CreatedSlot("shared-property-block-slot", "MainTexturePropertyBlock"));
            },
            (_, _, componentType, members, _) =>
            {
                createdComponentTypes.Add(componentType);
                if (componentType == "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock")
                {
                    Reference textureReference = Assert.IsType<Reference>(members["Texture"]);
                    Assert.Equal("shared-albedo-texture-component", textureReference.TargetID);
                    return Task.FromResult(new ResoniteLinkSceneBuilder.CreatedComponent("shared-property-block-component", componentType));
                }

                return Task.FromResult(new ResoniteLinkSceneBuilder.CreatedComponent("material-component", componentType));
            },
            static (_, _, _, _) => Task.FromResult<Slot?>(null),
            static (_, _, _) => Task.FromResult(new Uri("resdb:///texture/shared-albedo", UriKind.Absolute)));
        using StubResoniteLinkClient client = new();
        Dictionary<TextureReferenceKey, ResoniteTextureImport> preparedTextures = new()
        {
            [ResoniteMaterialAssetManager.CreateTextureReferenceKey("textures/albedo.png", ResoniteTextureSourceKind.Dataset)] =
                ResoniteTextureImportFactory.CreateFromFile("/tmp/albedo.png"),
        };

        CreatedMaterialAsset first = await manager.CreateMaterialComponentAsync(
            client,
            CreateMaterial(
                texturePath: "textures/albedo.png",
                textureSourceKind: ResoniteTextureSourceKind.Dataset,
                baseColor: new ResoniteColor(0.8, 0.8, 0.8, 1.0)),
            preparedTextures,
            "scope-slot",
            null,
            "Material",
            "property-block-scope",
            true,
            CancellationToken.None);
        CreatedMaterialAsset second = await manager.CreateMaterialComponentAsync(
            client,
            CreateMaterial(
                texturePath: "textures/albedo.png",
                textureSourceKind: ResoniteTextureSourceKind.Dataset,
                baseColor: new ResoniteColor(0.8, 0.8, 0.8, 1.0)),
            preparedTextures,
            "scope-slot",
            null,
            "Material",
            "property-block-scope",
            true,
            CancellationToken.None);

        Assert.Equal("shared-property-block-component", first.MaterialPropertyBlockComponentId);
        Assert.Equal("shared-property-block-component", second.MaterialPropertyBlockComponentId);
        Assert.Equal(2, sharedSlotCreationCount);
        Assert.Equal(
            1,
            createdComponentTypes.Count(static componentType =>
                string.Equals(componentType, "[FrooxEngine]FrooxEngine.MainTexturePropertyBlock", StringComparison.Ordinal)));
        Assert.Equal(
            1,
            createdComponentTypes.Count(static componentType =>
                string.Equals(componentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task CreateMaterialComponentAsyncDoesNotReuseSharedAlbedoPropertyBlockWhenImportedTextureChanges()
    {
        using TemporaryDirectory workingDirectory = new();
        string firstImportedTexturePath = Path.Combine(workingDirectory.Path, "albedo-v1.png");
        string secondImportedTexturePath = Path.Combine(workingDirectory.Path, "albedo-v2.png");
        await File.WriteAllBytesAsync(firstImportedTexturePath, [1, 2, 3, 4]);
        await File.WriteAllBytesAsync(secondImportedTexturePath, [5, 6, 7, 8]);

        List<string> createdComponentIds = [];
        int componentSequence = 0;
        ResoniteMaterialAssetManager manager = new(
            static (_, _, _, _, _) => throw new NotSupportedException(),
            (_, _, componentType, _, _) => Task.FromResult(
                new ResoniteLinkSceneBuilder.CreatedComponent(
                    $"{componentType}-{Interlocked.Increment(ref componentSequence)}",
                    componentType)),
            static (_, _, _, _) => Task.FromResult(new ResoniteLinkSceneBuilder.CreatedSlot("shared-slot", "Material")),
            (_, _, componentType, _, _) =>
            {
                string componentId = $"{componentType}-{Interlocked.Increment(ref componentSequence)}";
                createdComponentIds.Add(componentId);
                return Task.FromResult(new ResoniteLinkSceneBuilder.CreatedComponent(componentId, componentType));
            },
            static (_, _, _, _) => Task.FromResult<Slot?>(null),
            static (_, _, _) => Task.FromResult(new Uri("resdb:///texture/shared-albedo", UriKind.Absolute)));
        using StubResoniteLinkClient client = new();

        CreatedMaterialAsset first = await manager.CreateMaterialComponentAsync(
            client,
            CreateMaterial(
                texturePath: "textures/albedo.png",
                textureSourceKind: ResoniteTextureSourceKind.Dataset,
                baseColor: new ResoniteColor(0.8, 0.8, 0.8, 1.0)),
            new Dictionary<TextureReferenceKey, ResoniteTextureImport>
            {
                [ResoniteMaterialAssetManager.CreateTextureReferenceKey("textures/albedo.png", ResoniteTextureSourceKind.Dataset)] =
                    ResoniteTextureImportFactory.CreateFromFile(firstImportedTexturePath),
            },
            "scope-slot",
            null,
            "Material",
            "property-block-scope",
            true,
            CancellationToken.None);
        CreatedMaterialAsset second = await manager.CreateMaterialComponentAsync(
            client,
            CreateMaterial(
                texturePath: "textures/albedo.png",
                textureSourceKind: ResoniteTextureSourceKind.Dataset,
                baseColor: new ResoniteColor(0.8, 0.8, 0.8, 1.0)),
            new Dictionary<TextureReferenceKey, ResoniteTextureImport>
            {
                [ResoniteMaterialAssetManager.CreateTextureReferenceKey("textures/albedo.png", ResoniteTextureSourceKind.Dataset)] =
                    ResoniteTextureImportFactory.CreateFromFile(secondImportedTexturePath),
            },
            "scope-slot",
            null,
            "Material",
            "property-block-scope",
            true,
            CancellationToken.None);

        Assert.NotEqual(first.MaterialPropertyBlockComponentId, second.MaterialPropertyBlockComponentId);
        Assert.Equal(
            2,
            createdComponentIds.Count(componentId =>
                componentId.StartsWith("[FrooxEngine]FrooxEngine.MainTexturePropertyBlock", StringComparison.Ordinal)));
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
