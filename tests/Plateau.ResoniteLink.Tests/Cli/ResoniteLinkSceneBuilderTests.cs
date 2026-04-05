using System.Diagnostics.CodeAnalysis;

using Plateau.ResoniteLink.Application.Importing;
using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Tests.Cli;

[SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "The test helper owns builder disposal for all streaming execution paths.")]
public sealed class ResoniteLinkSceneBuilderTests
{
    [Fact]
    public async Task BuildAsyncImportsAssetsAndBuildsLiveComponents()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));

        using FakeResoniteLinkClient fakeClient = new();
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            () => fakeClient);

        IReadOnlyList<string> destinations = await RunBuilderAsync(builder, scene);

        Assert.Single(destinations);
        Assert.Equal(2, fakeClient.ImportedTexturePaths.Count);
        Assert.Equal(scene.CityObjects.Count, fakeClient.ImportedMeshes.Count);
        Assert.Contains(fakeClient.AddedComponents, static request =>
            string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.StaticMesh", StringComparison.Ordinal));
        Assert.Contains(fakeClient.AddedComponents, static request =>
            string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal));
        Assert.Contains(fakeClient.AddedComponents, static request =>
            string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal));
        Assert.Contains(fakeClient.AddedComponents, static request =>
            string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshCollider", StringComparison.Ordinal));
        Assert.Contains(fakeClient.AddedComponents, static request =>
            string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal));
        Assert.Contains(fakeClient.AddedComponents, static request =>
            string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.License", StringComparison.Ordinal));
        Assert.True(fakeClient.AssetSlotIds.ContainsKey("Assets"));
        Assert.True(fakeClient.AssetSlotIds.ContainsKey("Textures"));
        Assert.True(fakeClient.AssetSlotIds.ContainsKey("Meshes"));
        Assert.True(fakeClient.AssetSlotIds.ContainsKey("Materials"));
        Assert.True(fakeClient.AssetSlotIds.ContainsKey("53394525"));
        Assert.DoesNotContain(
            fakeClient.AddedSlots,
            request => string.Equals(request.Data.Name?.Value, "Building One Assets", StringComparison.Ordinal));

        IReadOnlyList<Component> staticMeshes = fakeClient.AddedComponents
            .Where(static request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.StaticMesh", StringComparison.Ordinal))
            .Select(static request => request.Data)
            .ToArray();
        Assert.All(staticMeshes, static component =>
        {
            Field_Uri url = Assert.IsType<Field_Uri>(component.Members["URL"]);
            Assert.StartsWith("resdb:///mesh/", url.Value.ToString(), StringComparison.Ordinal);
        });

        AddComponent[] staticTextureRequests = fakeClient.AddedComponents
            .Where(request => string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.StaticTexture2D", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, staticTextureRequests.Length);

        Assert.Contains(
            fakeClient.ImportedTexturePaths,
            path => string.Equals(
                path,
                Path.GetFullPath(Path.Combine(fixturePath, "udx/bldg/53394525/appearance/roof.png")),
                StringComparison.Ordinal));
        Assert.Contains(
            fakeClient.ImportedTexturePaths,
            path => BundledDefaultMaterialFamilies.FacadeVariants.Any(variant =>
                path.EndsWith(variant.Replace('/', Path.DirectorySeparatorChar), StringComparison.Ordinal)));

        AddComponent datasetTextureRequest = Assert.Single(
            staticTextureRequests,
            request =>
            {
                Field_Uri candidateUrl = Assert.IsType<Field_Uri>(request.Data.Members["URL"]);
                return string.Equals(candidateUrl.Value.ToString(), "resdb:///texture/0", StringComparison.Ordinal);
            });
        Slot textureAssetSlot = fakeClient.SlotsById[datasetTextureRequest.ContainerSlotId];
        Assert.Equal(fakeClient.AssetSlotIds["Textures"], textureAssetSlot.Parent.TargetID);
        Component datasetTexture = datasetTextureRequest.Data;
        Field_Uri datasetTextureUrl = Assert.IsType<Field_Uri>(datasetTexture.Members["URL"]);
        Assert.Equal("resdb:///texture/0", datasetTextureUrl.Value.ToString());

        AddComponent bundledTextureRequest = Assert.Single(
            staticTextureRequests,
            request =>
            {
                Field_Uri candidateUrl = Assert.IsType<Field_Uri>(request.Data.Members["URL"]);
                return string.Equals(candidateUrl.Value.ToString(), "resdb:///texture/1", StringComparison.Ordinal);
            });
        Slot bundledTextureAssetSlot = fakeClient.SlotsById[bundledTextureRequest.ContainerSlotId];
        Assert.Equal(fakeClient.AssetSlotIds["Textures"], bundledTextureAssetSlot.Parent.TargetID);
        Component bundledTexture = bundledTextureRequest.Data;
        Field_Uri bundledTextureUrl = Assert.IsType<Field_Uri>(bundledTexture.Members["URL"]);
        Assert.Equal("resdb:///texture/1", bundledTextureUrl.Value.ToString());

        Component license = Assert.Single(
            fakeClient.AddedComponents.Where(request =>
                    string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.License", StringComparison.Ordinal))
                .Select(static request => request.Data));
        Field_bool requireCredit = Assert.IsType<Field_bool>(license.Members["RequireCredit"]);
        Assert.True(requireCredit.Value);
        Field_string creditString = Assert.IsType<Field_string>(license.Members["CreditString"]);
        Assert.Contains(scene.Metadata.Attribution.DatasetLicense.LicenseName, creditString.Value, StringComparison.Ordinal);
        Assert.Contains(scene.Metadata.Attribution.DatasetLicense.LicenseUrl, creditString.Value, StringComparison.Ordinal);
        Assert.Contains(scene.Metadata.Attribution.DatasetLicense.CreditText, creditString.Value, StringComparison.Ordinal);

        AddComponent[] meshAssetRequests = fakeClient.AddedComponents
            .Where(request =>
                string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.StaticMesh", StringComparison.Ordinal)
                && string.Equals(
                    fakeClient.SlotsById[request.ContainerSlotId].Parent.TargetID,
                    fakeClient.AssetSlotIds["53394525"],
                    StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(meshAssetRequests);
        Assert.Contains(
            meshAssetRequests,
            request => string.Equals(
                fakeClient.SlotsById[request.ContainerSlotId].Name?.Value,
                "Mesh Building One",
                StringComparison.Ordinal));

        AddComponent[] materialRequests = fakeClient.AddedComponents
            .Where(request =>
                string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.PBS_Metallic", StringComparison.Ordinal)
                && request.ContainerSlotId != fakeClient.BuildingSlotIds["Building One"])
            .ToArray();
        Assert.Equal(2, materialRequests.Length);
        Assert.All(materialRequests, request =>
        {
            Slot materialAssetSlot = fakeClient.SlotsById[request.ContainerSlotId];
            Assert.Equal(fakeClient.AssetSlotIds["Materials"], materialAssetSlot.Parent.TargetID);
        });

        Component meshRenderer = Assert.Single(
            fakeClient.AddedComponents.Where(request =>
                    string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshRenderer", StringComparison.Ordinal)
                    && string.Equals(request.ContainerSlotId, fakeClient.BuildingSlotIds["Building One"], StringComparison.Ordinal))
                .Select(static request => request.Data));
        SyncList materials = Assert.IsType<SyncList>(meshRenderer.Members["Materials"]);
        Assert.Equal(2, materials.Elements.Count);

        Component collider = Assert.Single(
            fakeClient.AddedComponents.Where(request =>
                    string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.MeshCollider", StringComparison.Ordinal)
                    && string.Equals(request.ContainerSlotId, fakeClient.BuildingSlotIds["Building One"], StringComparison.Ordinal))
                .Select(static request => request.Data));
        Field_bool characterCollider = Assert.IsType<Field_bool>(collider.Members["CharacterCollider"]);
        Assert.True(characterCollider.Value);
    }

    [Fact]
    public async Task BuildAsyncUsesTriplanarMaterialForBundledRoadFallback()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDatasetMixedObjects");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));

        using FakeResoniteLinkClient fakeClient = new();
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            () => fakeClient);

        await RunBuilderAsync(builder, scene);

        Component[] triplanarMaterials = fakeClient.AddedComponents
            .Where(static request =>
                string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.PBS_TriplanarMetallic", StringComparison.Ordinal))
            .Select(static request => request.Data)
            .ToArray();
        Assert.NotEmpty(triplanarMaterials);
        Assert.All(
            triplanarMaterials,
            static triplanarMaterial =>
            {
                Assert.IsType<Field_float2>(triplanarMaterial.Members["TextureScale"]);
                Assert.IsType<Field_float2>(triplanarMaterial.Members["TextureOffset"]);
                Assert.IsType<Field_float>(triplanarMaterial.Members["TriplanarBlendPower"]);
                Assert.IsType<Field_bool>(triplanarMaterial.Members["ObjectSpace"]);
            });
    }

    [Fact]
    public async Task BuildAsyncAppliesMaterialDepthOffsetForTerrainAlignedOverlays()
    {
        CapturedResoniteScene scene = new(
            new ResoniteConstructionMetadata(
                SchemaVersion: "3.0",
                WorldName: "PLATEAU tokyo23ku 53394525",
                Request: new PlateauImportRequest(
                    Dataset: "tokyo23ku",
                    MeshCode: "53394525",
                    SourceKind: DatasetSourceKind.Local,
                    LocalSourcePath: TestData.GetFixturePath("LocalPlateauDataset"),
                    ServerUri: null),
                SourceDataset: new PlateauSourceDataset(
                    PackageNames: ["tran"],
                    SourceFiles: ["udx/tran/53394525/plateau_tokyo23ku_tran_53394525.gml"],
                    TerrainTextureOverlays: []),
                Attribution: new ResoniteAttribution(
                    DatasetLicense: new ResoniteLicenseComponentMetadata(
                        RequireCredit: true,
                        CreditText: "PLATEAU Open Data Terms",
                        LicenseName: "PLATEAU Open Data Terms",
                        LicenseUrl: "https://www.mlit.go.jp/plateau/site-policy/"),
                    MaterialLicenses: []),
                LocalOrigin: new ResoniteLocalOrigin(35.0, 139.0, 0.0)),
            [
                new ResoniteConstructionCityObject(
                    SlotKey: "terrain_road",
                    DisplayName: "Terrain Road",
                    PackageName: "tran",
                    Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
                    Mesh: new ResoniteImportedMesh(
                        Vertices:
                        [
                            new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                            new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                            new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 1.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                        ],
                        Submeshes:
                        [
                            new ResoniteMeshSubmesh(0, "terrain-road-material", [0, 1, 2]),
                        ]),
                    Materials:
                    [
                        new ResoniteMaterialBinding(
                            MaterialKey: "terrain-road-material",
                            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                            TexturePath: null,
                            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                            Projection: ResoniteMaterialProjection.Triplanar,
                            DepthOffset: LocalCityGmlResonitePlanBuilder.DefaultTerrainAlignedMaterialDepthOffset,
                            SubmeshIndices: [0]),
                    ]),
            ]);

        using FakeResoniteLinkClient fakeClient = new();
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            () => fakeClient);

        await RunBuilderAsync(builder, scene);

        Component material = Assert.Single(
            fakeClient.AddedComponents.Where(static request =>
                    string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.PBS_TriplanarMetallic", StringComparison.Ordinal))
                .Select(static request => request.Data));
        Field_float offsetFactor = Assert.IsType<Field_float>(material.Members["OffsetFactor"]);
        Field_float offsetUnits = Assert.IsType<Field_float>(material.Members["OffsetUnits"]);
        Assert.Equal((float)LocalCityGmlResonitePlanBuilder.DefaultTerrainAlignedMaterialDepthOffset.Factor, offsetFactor.Value);
        Assert.Equal((float)LocalCityGmlResonitePlanBuilder.DefaultTerrainAlignedMaterialDepthOffset.Units, offsetUnits.Value);
    }

    [Fact]
    public async Task BuildAsyncUsesUniqueEntityIdsAcrossRuns()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));

        using FakeResoniteLinkClient firstClient = new();
        using FakeResoniteLinkClient secondClient = new();

        await RunBuilderAsync(new ResoniteLinkSceneBuilder(new Uri("ws://localhost:12345/"), () => firstClient), scene);
        await RunBuilderAsync(new ResoniteLinkSceneBuilder(new Uri("ws://localhost:12345/"), () => secondClient), scene);

        HashSet<string> firstEntityIds = firstClient.AddedSlots
            .Select(static request => request.Data.ID)
            .Concat(firstClient.AddedComponents.Select(static request => request.Data.ID))
            .Where(static id => IsRunScopedEntityId(id))
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(
            secondClient.AddedSlots.Select(static request => request.Data.ID).Where(static id => IsRunScopedEntityId(id)),
            firstEntityIds.Contains);
        Assert.DoesNotContain(
            secondClient.AddedComponents.Select(static request => request.Data.ID).Where(static id => IsRunScopedEntityId(id)),
            firstEntityIds.Contains);
    }

    [Fact]
    public async Task BuildAsyncImportsGeneratedDemTerrainTexture()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDatasetMixedObjects");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));

        using FakeResoniteLinkClient fakeClient = new();
        StubTerrainTextureAssetGenerator terrainTextureAssetGenerator = new();
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            () => fakeClient,
            terrainTextureAssetGenerator);

        await RunBuilderAsync(builder, scene);

        TerrainTextureOverlay requestedOverlay = Assert.Single(terrainTextureAssetGenerator.RequestedOverlays);
        Assert.Equal(LocalCityGmlResonitePlanBuilder.DefaultDemTerrainTexturePath, requestedOverlay.TexturePath);

        string builtInTexturePath = Assert.Single(
            fakeClient.ImportedTexturePaths,
            static path => string.Equals(Path.GetFileName(path), "dem-overlay.png", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(builtInTexturePath));
    }

    [Fact]
    public async Task BuildAsyncReusesSharedAssetsWithinSession()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));

        FakeResoniteLinkSession session = new();
        using FakeResoniteLinkClient firstClient = new(session);
        using FakeResoniteLinkClient secondClient = new(session);

        await RunBuilderAsync(new ResoniteLinkSceneBuilder(new Uri("ws://localhost:12345/"), () => firstClient), scene);
        int importedTextureCountAfterFirstRun = firstClient.ImportedTexturePaths.Count;
        int importedMeshCountAfterFirstRun = firstClient.ImportedMeshes.Count;

        await RunBuilderAsync(new ResoniteLinkSceneBuilder(new Uri("ws://localhost:12345/"), () => secondClient), scene);

        Assert.Equal(importedTextureCountAfterFirstRun, secondClient.ImportedTexturePaths.Count);
        Assert.Equal(importedMeshCountAfterFirstRun, secondClient.ImportedMeshes.Count);
    }

    [Fact]
    public async Task ProcessCityObjectAsyncQueuesWorkBeforeLiveMeshImportCompletes()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        CapturedResoniteScene scene = LoadScene(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null));

        using BlockingResoniteLinkClient blockingClient = new();
        await using ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            () => blockingClient);

        await builder.BeginAsync(scene.Metadata, "runtime/resonite");
        await builder.ProcessCityObjectAsync(scene.CityObjects[0]);

        Task<IReadOnlyList<string>> completionTask = builder.CompleteAsync();
        Assert.False(completionTask.IsCompleted);

        blockingClient.ReleaseMeshImports();

        IReadOnlyList<string> destinations = await completionTask;
        Assert.Single(destinations);
    }

    private sealed class FakeResoniteLinkClient : IResoniteLinkClient
    {
        private readonly FakeResoniteLinkSession session;

        public FakeResoniteLinkClient()
            : this(new FakeResoniteLinkSession())
        {
        }

        public FakeResoniteLinkClient(FakeResoniteLinkSession session)
        {
            this.session = session;
        }

        public List<AddComponent> AddedComponents => session.AddedComponents;

        public List<AddSlot> AddedSlots => session.AddedSlots;

        public Dictionary<string, string> BuildingSlotIds => session.BuildingSlotIds;

        public Dictionary<string, string> AssetSlotIds => session.AssetSlotIds;

        public List<ImportMeshRawData> ImportedMeshes => session.ImportedMeshes;

        public List<string> ImportedTexturePaths => session.ImportedTexturePaths;

        public Dictionary<string, Slot> SlotsById => session.SlotsById;

        public void Dispose()
        {
        }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task AddComponentAsync(AddComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            session.ComponentsById[request.Data.ID] = request.Data;
            session.AddedComponents.Add(request);
            return Task.CompletedTask;
        }

        public Task AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            session.SlotsById[request.Data.ID] = request.Data;
            session.AddedSlots.Add(request);

            string? slotName = request.Data.Name?.Value;
            if (!string.IsNullOrWhiteSpace(slotName))
            {
                if (string.Equals(slotName, "Assets", StringComparison.Ordinal)
                    || string.Equals(slotName, "Textures", StringComparison.Ordinal)
                    || string.Equals(slotName, "Meshes", StringComparison.Ordinal)
                    || string.Equals(slotName, "Materials", StringComparison.Ordinal)
                    || slotName.All(char.IsAsciiDigit)
                    || slotName.StartsWith("Material ", StringComparison.Ordinal)
                    || slotName.EndsWith(" Assets", StringComparison.Ordinal))
                {
                    AssetSlotIds[slotName] = request.Data.ID;
                }
                else
                {
                    session.BuildingSlotIds[slotName] = request.Data.ID;
                }
            }

            return Task.CompletedTask;
        }

        public Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            session.ComponentsById.TryGetValue(componentId, out Component? component);
            return Task.FromResult(component);
        }

        public Task<Slot?> GetSlotAsync(string slotId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            session.SlotsById.TryGetValue(slotId, out Slot? slot);
            return Task.FromResult(slot);
        }

        public Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            session.ImportedMeshes.Add(request);
            return Task.FromResult(new Uri($"resdb:///mesh/{session.ImportedMeshes.Count - 1}", UriKind.Absolute));
        }

        public Task<Uri> ImportTextureAsync(string filePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            session.ImportedTexturePaths.Add(filePath);
            return Task.FromResult(new Uri($"resdb:///texture/{session.ImportedTexturePaths.Count - 1}", UriKind.Absolute));
        }

        public Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Component existing = session.ComponentsById[request.Data.ID];
            foreach ((string memberName, Member member) in request.Data.Members)
            {
                existing.Members[memberName] = member;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class StubTerrainTextureAssetGenerator : ITerrainTextureAssetGenerator
    {
        private static readonly byte[] TextureBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAGklEQVR42mP8z8DQwMDA8J+BkYGBgQEADzYCAjUX0xMAAAAASUVORK5CYII=");

        public List<TerrainTextureOverlay> RequestedOverlays { get; } = [];

        public async Task<string> EnsureTextureAsync(
            TerrainTextureOverlay terrainTextureOverlay,
            string workRoot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedOverlays.Add(terrainTextureOverlay);

            string textureDirectory = Path.Combine(workRoot, "terrain-textures");
            Directory.CreateDirectory(textureDirectory);
            string texturePath = Path.Combine(textureDirectory, "dem-overlay.png");
            if (!File.Exists(texturePath))
            {
                await File.WriteAllBytesAsync(texturePath, TextureBytes, cancellationToken);
            }

            return texturePath;
        }
    }

    private sealed class BlockingResoniteLinkClient : IResoniteLinkClient
    {
        private readonly TaskCompletionSource meshImportRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose()
        {
        }

        public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

        public Task<Component?> GetComponentAsync(string componentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Component?>(null);
        }

        public Task<Slot?> GetSlotAsync(string slotId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Slot?>(null);
        }

        public async Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
        {
            await meshImportRelease.Task.WaitAsync(cancellationToken);
            return new Uri("resdb:///mesh/0", UriKind.Absolute);
        }

        public Task<Uri> ImportTextureAsync(string filePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new Uri("resdb:///texture/0", UriKind.Absolute));
        }

        public void ReleaseMeshImports()
        {
            meshImportRelease.TrySetResult();
        }

        public Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeResoniteLinkSession
    {
        public List<AddComponent> AddedComponents { get; } = [];

        public List<AddSlot> AddedSlots { get; } = [];

        public Dictionary<string, string> AssetSlotIds { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> BuildingSlotIds { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Component> ComponentsById { get; } = new(StringComparer.Ordinal);

        public List<ImportMeshRawData> ImportedMeshes { get; } = [];

        public List<string> ImportedTexturePaths { get; } = [];

        public Dictionary<string, Slot> SlotsById { get; } = new(StringComparer.Ordinal);
    }

    private static bool IsRunScopedEntityId(string id)
    {
        return id.Contains("_meshcode_", StringComparison.Ordinal)
            || id.Contains("_cityobject_", StringComparison.Ordinal)
            || id.Contains("_renderer_", StringComparison.Ordinal)
            || id.Contains("_collider_", StringComparison.Ordinal)
            || id.Contains("_material_", StringComparison.Ordinal);
    }

    private static async Task<IReadOnlyList<string>> RunBuilderAsync(
        ResoniteLinkSceneBuilder builder,
        CapturedResoniteScene scene)
    {
        try
        {
            await builder.BeginAsync(scene.Metadata, "runtime/resonite");
            foreach (ResoniteConstructionCityObject cityObject in scene.CityObjects)
            {
                await builder.ProcessCityObjectAsync(cityObject);
            }

            return await builder.CompleteAsync();
        }
        finally
        {
            await builder.DisposeAsync();
        }
    }

    private static CapturedResoniteScene LoadScene(PlateauImportRequest request)
    {
        IResoniteConstructionSource source = LocalCityGmlResonitePlanBuilder.CreateConstructionSource(request);
        return new CapturedResoniteScene(source.Metadata, source.ReadCityObjects().ToArray());
    }

    private sealed record CapturedResoniteScene(
        ResoniteConstructionMetadata Metadata,
        IReadOnlyList<ResoniteConstructionCityObject> CityObjects);
}
