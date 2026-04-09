using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Plateau.ResoniteLink.Tests.Cli;

[Collection(BundledCompanionTextureIsolationGroup.Name)]
public sealed class ResoniteLinkSceneBuilderAssetReuseTests
{
    private const string DatasetName = "reuse-test";
    private const string MeshCode = "53394525";
    private static readonly SemaphoreSlim BundledCompanionTextureIsolationGate = new(1, 1);

    [Fact]
    public async Task BuildAsyncReimportsTriangleMeshWhenContentChangesInSameSession()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteConstructionMetadata metadata = CreateMetadata(datasetDirectory.Path);
        using ReuseSessionSharedClient sharedClient = new();

        ResoniteConstructionCityObject firstCityObject = CreateTriangleCityObject(
            objectIdentity: "shared-triangle",
            mesh: CreateTriangleMesh(0.0, 1.0, 2.0, "triangle-textured-material"));
        CapturedScene firstScene = new(
            metadata,
            [firstCityObject]);

        await BuildSceneOnceAsync(firstScene, sharedClient, Path.Combine(datasetDirectory.Path, "work"));
        int importedMeshesAfterFirstRun = sharedClient.ImportedMeshes.Count;

        ResoniteConstructionCityObject secondCityObject = CreateTriangleCityObject(
            objectIdentity: "shared-triangle",
            mesh: CreateTriangleMesh(3.0, 4.0, 5.0, "triangle-material"));
        CapturedScene secondScene = new(
            metadata,
            [secondCityObject]);

        await BuildSceneOnceAsync(secondScene, sharedClient, Path.Combine(datasetDirectory.Path, "work"));
        Assert.Equal(importedMeshesAfterFirstRun + 1, sharedClient.ImportedMeshes.Count);
    }

    [Fact]
    public async Task BuildAsyncReimportsRegularTextureWhenContentChangesInSameSession()
    {
        using TemporaryDirectory datasetDirectory = new();
        string texturePath = "textures/albedo.png";
        Directory.CreateDirectory(Path.Combine(datasetDirectory.Path, "textures"));
        await WriteSolidColorTextureAsync(
            Path.Combine(datasetDirectory.Path, texturePath),
            new Rgba32(255, 0, 0, 255));

        ResoniteConstructionMetadata metadata = CreateMetadata(
            datasetDirectory.Path,
            sourceFiles: [texturePath]);
        using ReuseSessionSharedClient sharedClient = new();

        ResoniteConstructionCityObject firstCityObject = CreateTexturedTriangleCityObject(
            objectIdentity: "shared-regular-texture",
            texturePath,
            mesh: CreateTriangleMesh(0.0, 1.0, 2.0, "triangle-textured-material"));
        CapturedScene firstScene = new(
            metadata,
            [firstCityObject]);

        await BuildSceneOnceAsync(firstScene, sharedClient, Path.Combine(datasetDirectory.Path, "work"));
        int importedTexturesAfterFirstRun = sharedClient.ImportedTexturePaths.Count;

        await WriteSolidColorTextureAsync(
            Path.Combine(datasetDirectory.Path, texturePath),
            new Rgba32(0, 255, 0, 255));

        ResoniteConstructionCityObject secondCityObject = CreateTexturedTriangleCityObject(
            objectIdentity: "shared-regular-texture",
            texturePath,
            mesh: CreateTriangleMesh(0.0, 1.0, 2.0, "triangle-textured-material"));
        CapturedScene secondScene = new(
            metadata,
            [secondCityObject]);

        await BuildSceneOnceAsync(secondScene, sharedClient, Path.Combine(datasetDirectory.Path, "work"));
        Assert.Equal(importedTexturesAfterFirstRun + 1, sharedClient.ImportedTexturePaths.Count);
    }

    [Fact]
    public async Task BuildAsyncReimportsHeightmapTextureWhenContentChangesInSameSession()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteConstructionMetadata metadata = CreateMetadata(datasetDirectory.Path, packageNames: ["terrain"]);
        using ReuseSessionSharedClient sharedClient = new();

        ResoniteConstructionCityObject firstCityObject = CreateHeightMapCityObject(
            objectIdentity: "shared-heightmap",
            heightSamples: [0, 1, 2, 3]);
        CapturedScene firstScene = new(
            metadata,
            [firstCityObject]);

        await BuildSceneOnceAsync(firstScene, sharedClient, Path.Combine(datasetDirectory.Path, "work"));
        int importedTexturesAfterFirstRun = sharedClient.ImportedRawHdrTextures.Count;

        ResoniteConstructionCityObject secondCityObject = CreateHeightMapCityObject(
            objectIdentity: "shared-heightmap",
            heightSamples: [3, 2, 1, 0]);
        CapturedScene secondScene = new(
            metadata,
            [secondCityObject]);

        await BuildSceneOnceAsync(secondScene, sharedClient, Path.Combine(datasetDirectory.Path, "work"));
        Assert.Equal(importedTexturesAfterFirstRun + 1, sharedClient.ImportedRawHdrTextures.Count);
    }

    [Fact]
    public async Task BuildAsyncReimportsBundledCompanionTexturesWhenContentsChangeInSameSession()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteConstructionMetadata metadata = CreateMetadata(datasetDirectory.Path, packageNames: ["bldg"]);
        using ReuseSessionSharedClient sharedClient = new();

        await BundledCompanionTextureIsolationGate.WaitAsync();

        try
        {
            using IDisposable extractionRootScope = BundledDefaultMaterialAssetStore.PushExtractionRootOverride(
                Path.Combine(datasetDirectory.Path, "bundled-assets"));
            string bundledTexturePath = BundledDefaultMaterialFamilies.FacadeVariants[0];
            ResoniteMaterialBinding sampleMaterial = new(
                MaterialKey: "bundle-companion-test",
                BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                MaterialType: ResoniteMaterialType.Standard,
                TexturePath: bundledTexturePath,
                TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                Projection: ResoniteMaterialProjection.Uv,
                DepthOffset: null,
                SubmeshIndices: [0]);
            Assert.True(ResoniteMaterialComponentBuilder.TryGetBundledCompanionTextureSet(sampleMaterial, out BundledDefaultMaterialTextureSet? bundledTextureSet));
            Assert.NotNull(bundledTextureSet);
            Assert.NotNull(bundledTextureSet.NormalPath);
            byte[] originalNormalBytes = await File.ReadAllBytesAsync(bundledTextureSet.NormalPath);

            try
            {
                ResoniteConstructionCityObject firstCityObject = CreateBundledTriangleCityObject(
                    objectIdentity: "shared-bundled-companion",
                    texturePath: bundledTexturePath,
                    mesh: CreateTriangleMesh(0.0, 1.0, 2.0, "triangle-textured-material"));
                CapturedScene firstScene = new(
                    metadata,
                    [firstCityObject]);

                await BuildSceneOnceAsync(firstScene, sharedClient, Path.Combine(datasetDirectory.Path, "work"));
                int importedTexturesAfterFirstRun = sharedClient.ImportedTexturePaths.Count;

                await WriteSolidColorTextureAsync(
                    bundledTextureSet.NormalPath,
                    new Rgba32(0, 0, 255, 255));

                ResoniteConstructionCityObject secondCityObject = CreateBundledTriangleCityObject(
                    objectIdentity: "shared-bundled-companion",
                    texturePath: bundledTexturePath,
                    mesh: CreateTriangleMesh(3.0, 4.0, 5.0, "triangle-textured-material"));
                CapturedScene secondScene = new(
                    metadata,
                    [secondCityObject]);

                await BuildSceneOnceAsync(secondScene, sharedClient, Path.Combine(datasetDirectory.Path, "work"));
                Assert.Equal(importedTexturesAfterFirstRun + 1, sharedClient.ImportedTexturePaths.Count);
            }
            finally
            {
                await File.WriteAllBytesAsync(bundledTextureSet.NormalPath, originalNormalBytes);
            }
        }
        finally
        {
            BundledCompanionTextureIsolationGate.Release();
        }
    }

    [Fact]
    public async Task BuildAsyncSharesCommonMaterialAssetsAcrossCityObjectsInSameSession()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteConstructionMetadata metadata = CreateMetadata(datasetDirectory.Path, packageNames: ["bldg"]);
        using ReuseSessionSharedClient sharedClient = new();

        string bundledTexturePath = BundledDefaultMaterialFamilies.FacadeVariants[0];
        CapturedScene scene = new(
            metadata,
            [
                CreateBundledTriangleCityObject(
                    objectIdentity: "shared-material-one",
                    texturePath: bundledTexturePath,
                    mesh: CreateTriangleMesh(0.0, 1.0, 2.0, "triangle-textured-material")),
                CreateBundledTriangleCityObject(
                    objectIdentity: "shared-material-two",
                    texturePath: bundledTexturePath,
                    mesh: CreateTriangleMesh(3.0, 4.0, 5.0, "triangle-textured-material")),
            ]);

        await BuildSceneOnceAsync(scene, sharedClient, Path.Combine(datasetDirectory.Path, "work"));

        Assert.Equal(
            1,
            sharedClient.AddedComponents.Count(static request =>
                string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal)));
        Assert.InRange(sharedClient.ImportedTexturePaths.Count, 4, 5);
    }

    [Fact]
    public async Task BuildAsyncPlacesMaterialAndTextureComponentsOnSameCommonAssetSlot()
    {
        using TemporaryDirectory datasetDirectory = new();
        ResoniteConstructionMetadata metadata = CreateMetadata(datasetDirectory.Path, packageNames: ["bldg"]);
        string bundledTexturePath = BundledDefaultMaterialFamilies.FacadeVariants[0];
        CapturedScene scene = new(
            metadata,
            [CreateBundledTriangleCityObject(
                objectIdentity: "same-slot-material-components",
                texturePath: bundledTexturePath,
                mesh: CreateTriangleMesh(0.0, 1.0, 2.0, "triangle-textured-material"))]);
        using ReuseSessionSharedClient client = new();

        await BuildSceneOnceAsync(scene, client, Path.Combine(datasetDirectory.Path, "work"));

        AddComponent materialRequest = Assert.Single(
            client.AddedComponents,
            static request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal));
        AddComponent[] textureRequests = client.AddedComponents
            .Where(static request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(textureRequests);
        Assert.All(textureRequests, request => Assert.Equal(materialRequest.ContainerSlotId, request.ContainerSlotId));
        Slot materialSlot = client.SlotsById[materialRequest.ContainerSlotId];
        Assert.Equal("triangle-textured-material", materialSlot.Name?.Value);
        Assert.NotNull(materialSlot.Parent);
        Slot parentSlot = client.SlotsById[materialSlot.Parent!.TargetID];
        Assert.Equal("Common", parentSlot.Name?.Value);
    }

    private static async Task BuildSceneOnceAsync(
        CapturedScene scene,
        ReuseSessionSharedClient client,
        string workRoot)
    {
        await using ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => client);

        await builder.BeginAsync(scene.Metadata, workRoot);
        foreach (ResoniteConstructionCityObject cityObject in scene.CityObjects)
        {
            await builder.ProcessCityObjectAsync(cityObject);
        }

        await builder.CompleteAsync();
    }

    private static async Task WriteSolidColorTextureAsync(string path, Rgba32 color)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using Image<Rgba32> image = new(2, 2, color);
        string extension = Path.GetExtension(path);
        if (string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            await image.SaveAsJpegAsync(path);
            return;
        }

        await image.SaveAsPngAsync(path);
    }

    private static ResoniteConstructionMetadata CreateMetadata(
        string datasetRoot,
        string[]? packageNames = null,
        string[]? sourceFiles = null)
    {
        string[] resolvedPackageNames = packageNames ?? ["bldg"];
        string[] resolvedSourceFiles = sourceFiles ?? [];

        return new ResoniteConstructionMetadata(
            SchemaVersion: "3.0",
            WorldName: $"PLATEAU {DatasetName} {MeshCode}",
            Request: new PlateauImportRequest(
                Dataset: DatasetName,
                MeshCode: MeshCode,
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: datasetRoot,
                ServerUri: null,
                PackageNames: resolvedPackageNames),
            SourceDataset: new PlateauSourceDataset(
                PackageNames: resolvedPackageNames,
                SourceFiles: resolvedSourceFiles,
                TerrainTextureOverlays: []),
            Attribution: new ResoniteAttribution(
                DatasetLicense: new ResoniteLicenseComponentMetadata(
                    RequireCredit: true,
                    CreditText: "Test license",
                    LicenseName: "Test",
                    LicenseUrl: "https://example.com/license"),
                MaterialLicenses: []),
            LocalOrigin: new ResoniteLocalOrigin(35.0, 139.0, 0.0));
    }

    private static ResoniteConstructionCityObject CreateTriangleCityObject(
        string objectIdentity,
        ResoniteImportedMesh mesh)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: $"slot-{objectIdentity}",
            DisplayName: $"CityObject {objectIdentity}",
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: mesh,
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "triangle-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
            ],
            SourceObjectKey: objectIdentity);
    }

    private static ResoniteConstructionCityObject CreateTexturedTriangleCityObject(
        string objectIdentity,
        string texturePath,
        ResoniteImportedMesh mesh)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: $"slot-{objectIdentity}",
            DisplayName: $"CityObject {objectIdentity}",
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: mesh,
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "triangle-textured-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: texturePath,
                    TextureSourceKind: ResoniteTextureSourceKind.Dataset,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
            ],
            SourceObjectKey: objectIdentity);
    }

    private static ResoniteConstructionCityObject CreateBundledTriangleCityObject(
        string objectIdentity,
        string texturePath,
        ResoniteImportedMesh mesh)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: $"slot-{objectIdentity}",
            DisplayName: $"CityObject {objectIdentity}",
            PackageName: "bldg",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Mesh: mesh,
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "triangle-textured-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: texturePath,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
            ],
            SourceObjectKey: objectIdentity);
    }

    private static ResoniteConstructionCityObject CreateHeightMapCityObject(
        string objectIdentity,
        IReadOnlyList<double> heightSamples)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: $"slot-{objectIdentity}",
            DisplayName: $"CityObject {objectIdentity}",
            PackageName: "terrain",
            ActualMeshCode: MeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Geometry: new ResoniteHeightMapGridGeometry(
                Width: 2,
                Height: 2,
                Size: new ResoniteFloat2(10.0, 10.0),
                MinHeight: 0.0,
                MaxHeight: 3.0,
                HeightSamples: heightSamples),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "heightmap-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
            ],
            SourceObjectKey: objectIdentity);
    }

    private static ResoniteImportedMesh CreateTriangleMesh(double firstY, double secondY, double thirdY)
    {
        return CreateTriangleMesh(firstY, secondY, thirdY, "triangle-material");
    }

    private static ResoniteImportedMesh CreateTriangleMesh(
        double firstY,
        double secondY,
        double thirdY,
        string materialKey)
    {
        return new ResoniteImportedMesh(
            Vertices:
            [
                new ResoniteMeshVertex(new ResoniteFloat3(0.0, firstY, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(1.0, secondY, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(0.0, thirdY, 1.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
            ],
            Submeshes:
            [
                new ResoniteMeshSubmesh(0, materialKey, [0, 1, 2]),
            ]);
    }

    private sealed class ReuseSessionSharedClient : IResoniteLinkClient
    {
        private readonly ReuseFakeSession session;

        public ReuseSessionSharedClient()
            : this(new ReuseFakeSession())
        {
        }

        public ReuseSessionSharedClient(ReuseFakeSession session)
        {
            this.session = session;
        }

        public int ConnectCallCount { get; private set; }
        public List<AddComponent> AddedComponents => session.AddedComponents;
        public List<AddSlot> AddedSlots => session.AddedSlots;
        public List<ImportMeshRawData> ImportedMeshes => session.ImportedMeshes;
        public List<string> ImportedTexturePaths => session.ImportedTexturePaths;
        public List<ResoniteRawTextureImport> ImportedRawTextures => session.ImportedRawTextures;
        public List<ResoniteRawHdrTextureImport> ImportedRawHdrTextures => session.ImportedRawHdrTextures;
        public Dictionary<string, Component> ComponentsById => session.ComponentsById;
        public Dictionary<string, Slot> SlotsById => session.SlotsById;
        public List<IReadOnlyList<DataModelOperation>> Batches => session.Batches;

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
            lock (session.Gate)
            {
                session.ComponentsById[request.Data.ID] = request.Data;
                session.AddedComponents.Add(request);
            }

            return Task.CompletedTask;
        }

        public Task AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                session.SlotsById[request.Data.ID] = request.Data;
                session.AddedSlots.Add(request);
            }

            return Task.CompletedTask;
        }

        public async Task RunDataModelOperationBatchAsync(
            IReadOnlyList<DataModelOperation> operations,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                session.Batches.Add(operations.ToArray());
            }

            foreach (DataModelOperation operation in operations)
            {
                switch (operation)
                {
                    case AddSlot addSlot:
                        await AddSlotAsync(addSlot, cancellationToken);
                        break;
                    case AddComponent addComponent:
                        await AddComponentAsync(addComponent, cancellationToken);
                        break;
                    case UpdateComponent updateComponent:
                        await UpdateComponentAsync(updateComponent, cancellationToken);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported batch operation '{operation.GetType().Name}'.");
                }
            }
        }

        public Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Component? component;
            lock (session.Gate)
            {
                session.ComponentsById.TryGetValue(componentId, out component);
            }

            return Task.FromResult(component);
        }

        public Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Slot? slot;
            lock (session.Gate)
            {
                session.SlotsById.TryGetValue(slotId, out slot);
            }

            return Task.FromResult(slot is null ? null : CloneSlot(slot, depth));
        }

        public Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                session.ImportedMeshes.Add(request);
                return Task.FromResult(new Uri($"resdb:///mesh/{session.ImportedMeshes.Count - 1}", UriKind.Absolute));
            }
        }

        public Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                switch (textureImport)
                {
                    case ResoniteFileTextureImport fileImport:
                        session.ImportedTexturePaths.Add(fileImport.AbsolutePath);
                        break;
                    case ResoniteRawTextureImport rawImport:
                        session.ImportedRawTextures.Add(rawImport);
                        if (rawImport.SourcePath is not null)
                        {
                            session.ImportedTexturePaths.Add(rawImport.SourcePath);
                        }

                        break;
                    case ResoniteRawHdrTextureImport rawHdrImport:
                        session.ImportedRawHdrTextures.Add(rawHdrImport);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported texture import type '{textureImport.GetType().Name}'.");
                }

                return Task.FromResult(new Uri($"resdb:///texture/{session.ImportedTexturePaths.Count + session.ImportedRawTextures.Count + session.ImportedRawHdrTextures.Count - 1}", UriKind.Absolute));
            }
        }

        public Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (session.Gate)
            {
                if (!session.ComponentsById.TryGetValue(request.Data.ID, out Component? existingComponent))
                {
                    return Task.CompletedTask;
                }

                foreach ((string memberName, Member member) in request.Data.Members)
                {
                    existingComponent.Members[memberName] = member;
                }
            }

            return Task.CompletedTask;
        }

        private static Slot CloneSlot(Slot source, int depth)
        {
            Slot clone = new()
            {
                ID = source.ID,
                Parent = source.Parent,
                Name = source.Name,
                Position = source.Position,
                Components = source.Components,
                Rotation = source.Rotation,
            };

            if (depth <= 0)
            {
                return clone;
            }

            return clone;
        }
    }

    private sealed class ReuseFakeSession
    {
        public object Gate { get; } = new();

        public List<AddComponent> AddedComponents { get; } = [];

        public List<AddSlot> AddedSlots { get; } = [];

        public List<ImportMeshRawData> ImportedMeshes { get; } = [];

        public List<string> ImportedTexturePaths { get; } = [];

        public List<ResoniteRawTextureImport> ImportedRawTextures { get; } = [];

        public List<ResoniteRawHdrTextureImport> ImportedRawHdrTextures { get; } = [];

        public List<IReadOnlyList<DataModelOperation>> Batches { get; } = [];

        public Dictionary<string, Component> ComponentsById { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Slot> SlotsById { get; } = new(StringComparer.Ordinal);
    }

    private sealed record CapturedScene(
        ResoniteConstructionMetadata Metadata,
        IReadOnlyList<ResoniteConstructionCityObject> CityObjects);

}

[CollectionDefinition(BundledCompanionTextureIsolationGroup.Name, DisableParallelization = true)]
public sealed class BundledCompanionTextureIsolationGroup
{
    public const string Name = "BundledCompanionTextureIsolation";
}
