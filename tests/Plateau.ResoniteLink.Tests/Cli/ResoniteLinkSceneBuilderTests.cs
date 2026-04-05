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
        ResoniteConstructionPlan plan = LocalCityGmlResonitePlanBuilder.BuildPlan(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                InputPath: fixturePath,
                ServerUri: null));

        using FakeResoniteLinkClient fakeClient = new();
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            () => fakeClient);

        IReadOnlyList<string> destinations = await RunBuilderAsync(builder, plan);

        Assert.Single(destinations);
        Assert.Equal(2, fakeClient.ImportedTexturePaths.Count);
        Assert.Equal(plan.CityObjects.Count, fakeClient.ImportedMeshes.Count);
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
        Assert.True(fakeClient.AssetSlotIds.ContainsKey("Building One Assets"));

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
        Assert.Contains(plan.Attribution.DatasetLicense.LicenseName, creditString.Value, StringComparison.Ordinal);
        Assert.Contains(plan.Attribution.DatasetLicense.LicenseUrl, creditString.Value, StringComparison.Ordinal);
        Assert.Contains(plan.Attribution.DatasetLicense.CreditText, creditString.Value, StringComparison.Ordinal);

        Assert.Contains(
            fakeClient.AddedComponents,
            request =>
                string.Equals(request.ContainerSlotId, fakeClient.AssetSlotIds["Building One Assets"], StringComparison.Ordinal)
                && string.Equals(request.Data.ComponentType, "[FrooxEngine]FrooxEngine.StaticMesh", StringComparison.Ordinal));
        Assert.Equal(
            fakeClient.BuildingSlotIds["Building One"],
            fakeClient.SlotsById[fakeClient.AssetSlotIds["Building One Assets"]].Parent.TargetID);

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
        ResoniteConstructionPlan plan = LocalCityGmlResonitePlanBuilder.BuildPlan(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                InputPath: fixturePath,
                ServerUri: null));

        using FakeResoniteLinkClient fakeClient = new();
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            () => fakeClient);

        await RunBuilderAsync(builder, plan);

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
    public async Task BuildAsyncUsesUniqueEntityIdsAcrossRuns()
    {
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        ResoniteConstructionPlan plan = LocalCityGmlResonitePlanBuilder.BuildPlan(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                InputPath: fixturePath,
                ServerUri: null));

        using FakeResoniteLinkClient firstClient = new();
        using FakeResoniteLinkClient secondClient = new();

        await RunBuilderAsync(new ResoniteLinkSceneBuilder(new Uri("ws://localhost:12345/"), () => firstClient), plan);
        await RunBuilderAsync(new ResoniteLinkSceneBuilder(new Uri("ws://localhost:12345/"), () => secondClient), plan);

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
        ResoniteConstructionPlan plan = LocalCityGmlResonitePlanBuilder.BuildPlan(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                InputPath: fixturePath,
                ServerUri: null));

        using FakeResoniteLinkClient fakeClient = new();
        StubTerrainTextureAssetGenerator terrainTextureAssetGenerator = new();
        ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            () => fakeClient,
            terrainTextureAssetGenerator);

        await RunBuilderAsync(builder, plan);

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
        ResoniteConstructionPlan plan = LocalCityGmlResonitePlanBuilder.BuildPlan(
            new PlateauImportRequest(
                Dataset: "tokyo23ku",
                MeshCode: "53394525",
                SourceKind: DatasetSourceKind.Local,
                InputPath: fixturePath,
                ServerUri: null));

        FakeResoniteLinkSession session = new();
        using FakeResoniteLinkClient firstClient = new(session);
        using FakeResoniteLinkClient secondClient = new(session);

        await RunBuilderAsync(new ResoniteLinkSceneBuilder(new Uri("ws://localhost:12345/"), () => firstClient), plan);
        int importedTextureCountAfterFirstRun = firstClient.ImportedTexturePaths.Count;
        int importedMeshCountAfterFirstRun = firstClient.ImportedMeshes.Count;

        await RunBuilderAsync(new ResoniteLinkSceneBuilder(new Uri("ws://localhost:12345/"), () => secondClient), plan);

        Assert.Equal(importedTextureCountAfterFirstRun, secondClient.ImportedTexturePaths.Count);
        Assert.Equal(importedMeshCountAfterFirstRun, secondClient.ImportedMeshes.Count);
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
            string outputRoot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedOverlays.Add(terrainTextureOverlay);

            string textureDirectory = Path.Combine(outputRoot, "terrain-textures");
            Directory.CreateDirectory(textureDirectory);
            string texturePath = Path.Combine(textureDirectory, "dem-overlay.png");
            if (!File.Exists(texturePath))
            {
                await File.WriteAllBytesAsync(texturePath, TextureBytes, cancellationToken);
            }

            return texturePath;
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
        ResoniteConstructionPlan plan)
    {
        try
        {
            ResoniteConstructionMetadata metadata = new(
                plan.SchemaVersion,
                plan.WorldName,
                plan.Request,
                plan.SourceDataset,
                plan.Attribution,
                plan.LocalOrigin);

            await builder.BeginAsync(metadata, "artifacts/resonite");
            foreach (ResoniteConstructionCityObject cityObject in plan.CityObjects)
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
}
