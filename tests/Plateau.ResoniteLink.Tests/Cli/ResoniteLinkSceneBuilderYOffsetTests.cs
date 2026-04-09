using System.Globalization;

using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

using ResoniteLink;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class ResoniteLinkSceneBuilderYOffsetTests
{
    [Fact]
    public async Task BeginAndBuildPreservesCityObjectHeightWithoutDatasetLift()
    {
        const string dataset = "tokyo23ku";
        const string meshCode = "53394525";
        const string sourceObjectKey = "plateau_test_building";
        const double buildingHeight = 15.5;

        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        ResoniteYTestScene scene = CreateScene(dataset, meshCode, sourceObjectKey, buildingHeight, fixturePath);

        using YOffsetFakeClient fakeClient = new();
        await using ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => fakeClient);

        using TemporaryDirectory workDirectory = new();
        await builder.BeginAsync(scene.Metadata, workDirectory.Path);
        foreach (ResoniteConstructionCityObject cityObject in scene.CityObjects)
        {
            await builder.ProcessCityObjectAsync(cityObject);
        }

        _ = await builder.CompleteAsync();

        string datasetSlotId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(dataset, "dataset");
        Slot datasetSlot = Assert.IsType<Slot>(fakeClient.SlotsById[datasetSlotId]);
        Field_float3 datasetPosition = Assert.IsType<Field_float3>(datasetSlot.Position);
        Assert.Equal(0.0f, datasetPosition.Value.y);

        string cityObjectSlotId = ResoniteLinkEntityIdFactory.CreateStableEntityId(
            dataset,
            meshCode,
            "cityobject",
            sourceObjectKey);
        Slot cityObjectSlot = Assert.IsType<Slot>(fakeClient.SlotsById[cityObjectSlotId]);
        Field_float3 cityObjectPosition = Assert.IsType<Field_float3>(cityObjectSlot.Position);
        Assert.Equal((float)buildingHeight, cityObjectPosition.Value.y);
    }

    [Fact]
    public async Task BeginAsyncResetsExistingDatasetRootYOffsetToZero()
    {
        const string dataset = "tokyo23ku";
        const string meshCode = "53394525";
        const string sourceObjectKey = "plateau_test_building";

        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        ResoniteYTestScene scene = CreateScene(dataset, meshCode, sourceObjectKey, 15.5, fixturePath);
        string datasetSlotId = ResoniteLinkEntityIdFactory.CreateDatasetScopedEntityId(dataset, "dataset");

        using YOffsetFakeClient fakeClient = new();
        fakeClient.SlotsById[datasetSlotId] = new Slot
        {
            ID = datasetSlotId,
            Parent = new Reference { TargetID = "Root" },
            Name = new Field_string { Value = $"PLATEAU {dataset}" },
            Position = new Field_float3
            {
                Value = new float3
                {
                    x = 0.0f,
                    y = 1.5f,
                    z = 0.0f,
                },
            },
        };

        await using ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => fakeClient);

        using TemporaryDirectory workDirectory = new();
        await builder.BeginAsync(scene.Metadata, workDirectory.Path);

        Slot datasetSlot = Assert.IsType<Slot>(fakeClient.SlotsById[datasetSlotId]);
        Field_float3 datasetPosition = Assert.IsType<Field_float3>(datasetSlot.Position);
        Assert.Equal(0.0f, datasetPosition.Value.y);
    }

    [Fact]
    public async Task BuildAsyncSendsHeightMapAsHdrRawTexture()
    {
        const string dataset = "tokyo23ku";
        const string meshCode = "53394525";
        const string sourceObjectKey = "dangerous/slot:key";

        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        ResoniteConstructionMetadata metadata = CreateMetadata(dataset, meshCode, fixturePath);
        ResoniteConstructionCityObject cityObject = new(
            SlotKey: sourceObjectKey,
            DisplayName: "Height Test Terrain",
            PackageName: "dem",
            ActualMeshCode: meshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, 0.0, 0.0)),
            Geometry: new ResoniteHeightMapGridGeometry(
                Width: 2,
                Height: 2,
                Size: new ResoniteFloat2(10.0, 10.0),
                MinHeight: 0.0,
                MaxHeight: 3.0,
                HeightSamples: [0.0, 1.0, 2.0, 3.0]),
            Materials: [CreateWireframeMaterial()],
            SourceObjectKey: sourceObjectKey);

        using YOffsetFakeClient fakeClient = new();
        string workRoot = Path.Combine(Path.GetTempPath(), $"resonite-heightmap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workRoot);

        try
        {
            await using ResoniteLinkSceneBuilder builder = new(
                new Uri("ws://localhost:12345/"),
                1,
                ResoniteLinkSendDiagnostics.Disabled,
                () => fakeClient);

            await builder.BeginAsync(metadata, workRoot);
            await builder.ProcessCityObjectAsync(cityObject);
            _ = await builder.CompleteAsync();

            ResoniteRawHdrTextureImport importedTexture = Assert.Single(fakeClient.ImportedRawHdrTextures);
            Assert.Equal(2, importedTexture.Width);
            Assert.Equal(2, importedTexture.Height);
            Assert.Empty(fakeClient.ImportedTexturePaths);
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    [Fact]
    public async Task BuildAsyncKeepsRegexHeightMapAndBuildingAlignedInWorldYAcrossMeshRoots()
    {
        const string dataset = "tokyo23ku";
        const string regexMeshCode = "5339452[56]";
        const string requestedMeshCode = "53394525";
        const double expectedWorldY = 12.5;
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        ResoniteConstructionMetadata metadata = CreateMetadata(
            dataset,
            regexMeshCode,
            fixturePath,
            requestedMeshCodes: [requestedMeshCode]);

        ResoniteConstructionCityObject demCityObject = new(
            SlotKey: "dem-heightmap",
            DisplayName: "Regex HeightMap Terrain",
            PackageName: "dem",
            ActualMeshCode: "533945",
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, expectedWorldY, 0.0)),
            Geometry: new ResoniteHeightMapGridGeometry(
                Width: 2,
                Height: 2,
                Size: new ResoniteFloat2(10.0, 10.0),
                MinHeight: 0.0,
                MaxHeight: 3.0,
                HeightSamples: [0.0, 1.0, 2.0, 3.0]),
            Materials: [CreateWireframeMaterial()],
            SourceObjectKey: "dem-heightmap-source");
        ResoniteConstructionCityObject buildingCityObject = new(
            SlotKey: "regex-building",
            DisplayName: "Regex Building",
            PackageName: "bldg",
            ActualMeshCode: requestedMeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(1.0, expectedWorldY, 2.5)),
            Mesh: CreateTriangleMesh(),
            Materials: [CreateWireframeMaterial()],
            SourceObjectKey: "regex-building-source");

        using YOffsetFakeClient fakeClient = new();
        await using ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => fakeClient);

        using TemporaryDirectory workDirectory = new();
        await builder.BeginAsync(metadata, workDirectory.Path);
        await builder.ProcessCityObjectAsync(demCityObject);
        await builder.ProcessCityObjectAsync(buildingCityObject);
        _ = await builder.CompleteAsync();

        string demRootSlotId = ResoniteLinkEntityIdFactory.CreateStableEntityId(dataset, "533945", "meshcode");
        string buildingRootSlotId = ResoniteLinkEntityIdFactory.CreateStableEntityId(dataset, requestedMeshCode, "meshcode");
        string demLodSlotId = ResoniteLinkEntityIdFactory.CreateStableEntityId(dataset, "533945", "lod", "dem", "LOD0");
        string buildingCityObjectSlotId = ResoniteLinkEntityIdFactory.CreateStableEntityId(dataset, requestedMeshCode, "cityobject", "regex-building-source");

        Slot demCityObjectSlot = FindSlotByNameUnderAncestor(
            fakeClient.SlotsById,
            demLodSlotId,
            "Regex HeightMap Terrain");
        Slot demRootSlot = fakeClient.SlotsById[demRootSlotId];
        Slot buildingRootSlot = fakeClient.SlotsById[buildingRootSlotId];
        Slot buildingCityObjectSlot = fakeClient.SlotsById[buildingCityObjectSlotId];
        double demWorldY = GetWorldY(demRootSlot, demCityObjectSlot);
        double buildingWorldY = GetWorldY(buildingRootSlot, buildingCityObjectSlot);

        Assert.True(
            Math.Abs(expectedWorldY - demWorldY) < 0.000001,
            string.Create(
                CultureInfo.InvariantCulture,
                $"DEM world Y drifted for regex request. expected={expectedWorldY:F6}, actual={demWorldY:F6}, rootY={GetSlotY(demRootSlot):F6}, localY={GetSlotY(demCityObjectSlot):F6}"));
        Assert.True(
            Math.Abs(expectedWorldY - buildingWorldY) < 0.000001,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Building world Y drifted for regex request. expected={expectedWorldY:F6}, actual={buildingWorldY:F6}, rootY={GetSlotY(buildingRootSlot):F6}, localY={GetSlotY(buildingCityObjectSlot):F6}"));
    }

    [Fact]
    public async Task BuildAsyncKeepsMeshModeWorldYAlignedAcrossMeshRoots()
    {
        const string dataset = "tokyo23ku";
        const string regexMeshCode = "5339452[56]";
        const string requestedMeshCode = "53394525";
        const double expectedWorldY = 18.25;
        string fixturePath = TestData.GetFixturePath("LocalPlateauDataset");
        ResoniteConstructionMetadata metadata = CreateMetadata(
            dataset,
            regexMeshCode,
            fixturePath,
            requestedMeshCodes: [requestedMeshCode]);

        ResoniteConstructionCityObject parentMeshBuilding = new(
            SlotKey: "parent-mesh-building",
            DisplayName: "Parent Mesh Building",
            PackageName: "bldg",
            ActualMeshCode: "533945",
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(0.0, expectedWorldY, 0.0)),
            Mesh: CreateTriangleMesh(),
            Materials: [CreateWireframeMaterial()],
            SourceObjectKey: "parent-mesh-building-source");
        ResoniteConstructionCityObject detailedMeshBuilding = new(
            SlotKey: "detailed-mesh-building",
            DisplayName: "Detailed Mesh Building",
            PackageName: "bldg",
            ActualMeshCode: requestedMeshCode,
            LodLevel: 0,
            Transform: new ResoniteTransform(new ResoniteFloat3(1.0, expectedWorldY, 2.5)),
            Mesh: CreateTriangleMesh(),
            Materials: [CreateWireframeMaterial()],
            SourceObjectKey: "detailed-mesh-building-source");

        using YOffsetFakeClient fakeClient = new();
        await using ResoniteLinkSceneBuilder builder = new(
            new Uri("ws://localhost:12345/"),
            1,
            ResoniteLinkSendDiagnostics.Disabled,
            () => fakeClient);

        using TemporaryDirectory workDirectory = new();
        await builder.BeginAsync(metadata, workDirectory.Path);
        await builder.ProcessCityObjectAsync(parentMeshBuilding);
        await builder.ProcessCityObjectAsync(detailedMeshBuilding);
        _ = await builder.CompleteAsync();

        string parentRootSlotId = ResoniteLinkEntityIdFactory.CreateStableEntityId(dataset, "533945", "meshcode");
        string detailedRootSlotId = ResoniteLinkEntityIdFactory.CreateStableEntityId(dataset, requestedMeshCode, "meshcode");
        string parentCityObjectSlotId = ResoniteLinkEntityIdFactory.CreateStableEntityId(dataset, "533945", "cityobject", "parent-mesh-building-source");
        string detailedCityObjectSlotId = ResoniteLinkEntityIdFactory.CreateStableEntityId(dataset, requestedMeshCode, "cityobject", "detailed-mesh-building-source");

        Slot parentRootSlot = fakeClient.SlotsById[parentRootSlotId];
        Slot detailedRootSlot = fakeClient.SlotsById[detailedRootSlotId];
        Slot parentCityObjectSlot = fakeClient.SlotsById[parentCityObjectSlotId];
        Slot detailedCityObjectSlot = fakeClient.SlotsById[detailedCityObjectSlotId];
        double parentWorldY = GetWorldY(parentRootSlot, parentCityObjectSlot);
        double detailedWorldY = GetWorldY(detailedRootSlot, detailedCityObjectSlot);

        Assert.True(
            Math.Abs(expectedWorldY - parentWorldY) < 0.000001,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Parent mesh world Y drifted. expected={expectedWorldY:F6}, actual={parentWorldY:F6}, rootY={GetSlotY(parentRootSlot):F6}, localY={GetSlotY(parentCityObjectSlot):F6}"));
        Assert.True(
            Math.Abs(expectedWorldY - detailedWorldY) < 0.000001,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Detailed mesh world Y drifted. expected={expectedWorldY:F6}, actual={detailedWorldY:F6}, rootY={GetSlotY(detailedRootSlot):F6}, localY={GetSlotY(detailedCityObjectSlot):F6}"));
        Assert.Equal(0.0, GetSlotY(parentRootSlot));
        Assert.Equal(0.0, GetSlotY(detailedRootSlot));
    }

    private static ResoniteConstructionMetadata CreateMetadata(
        string dataset,
        string meshCode,
        string fixturePath,
        IReadOnlyList<string>? requestedMeshCodes = null)
    {
        return new ResoniteConstructionMetadata(
            SchemaVersion: "3.0",
            WorldName: $"PLATEAU {dataset} {meshCode}",
            Request: new PlateauImportRequest(
                Dataset: dataset,
                MeshCode: meshCode,
                SourceKind: DatasetSourceKind.Local,
                LocalSourcePath: fixturePath,
                ServerUri: null),
            SourceDataset: new PlateauSourceDataset(
                PackageNames: ["bldg"],
                SourceFiles:
                [
                    $"udx/bldg/{meshCode}/plateau_{dataset}_bldg_{meshCode}.gml",
                ],
                TerrainTextureOverlays: [],
                RequestedMeshCodes: requestedMeshCodes),
            Attribution: new ResoniteAttribution(
                DatasetLicense: new ResoniteLicenseComponentMetadata(
                    RequireCredit: true,
                    CreditText: "PLATEAU Open Data Terms",
                    LicenseName: "PLATEAU Open Data Terms",
                    LicenseUrl: "https://www.mlit.go.jp/plateau/site-policy/"),
                MaterialLicenses: []),
            LocalOrigin: new ResoniteLocalOrigin(35.6875, 139.69375, 0.0));
    }

    private static double GetWorldY(Slot rootSlot, Slot cityObjectSlot)
    {
        return GetSlotY(rootSlot) + GetSlotY(cityObjectSlot);
    }

    private static double GetSlotY(Slot slot)
    {
        Field_float3 position = Assert.IsType<Field_float3>(slot.Position);
        return position.Value.y;
    }

    private static Slot FindSlotByNameUnderAncestor(
        IReadOnlyDictionary<string, Slot> slotsById,
        string ancestorSlotId,
        string slotName)
    {
        return Assert.Single(
            slotsById.Values,
            slot =>
                string.Equals(slot.Name?.Value, slotName, StringComparison.Ordinal)
                && IsDescendantOf(slotsById, slot, ancestorSlotId));
    }

    private static bool IsDescendantOf(
        IReadOnlyDictionary<string, Slot> slotsById,
        Slot slot,
        string ancestorSlotId)
    {
        Reference? parent = slot.Parent;
        while (parent is not null && slotsById.TryGetValue(parent.TargetID, out Slot? parentSlot))
        {
            if (string.Equals(parentSlot.ID, ancestorSlotId, StringComparison.Ordinal))
            {
                return true;
            }

            parent = parentSlot.Parent;
        }

        return false;
    }

    private static ResoniteYTestScene CreateScene(
        string dataset,
        string meshCode,
        string sourceObjectKey,
        double height,
        string fixturePath)
    {
        ResoniteConstructionMetadata metadata = CreateMetadata(dataset, meshCode, fixturePath);

        return new ResoniteYTestScene(
            metadata,
            [
                new ResoniteConstructionCityObject(
                    SlotKey: sourceObjectKey,
                    DisplayName: "Height Test Building",
                    PackageName: "bldg",
                    ActualMeshCode: meshCode,
                    LodLevel: 0,
                    Transform: new ResoniteTransform(new ResoniteFloat3(1.0, height, 2.5)),
                    Mesh: CreateTriangleMesh(),
                    Materials: [CreateWireframeMaterial()],
                    SourceObjectKey: sourceObjectKey),
            ]);
    }

    private static ResoniteMaterialBinding CreateWireframeMaterial()
    {
        return new ResoniteMaterialBinding(
            MaterialKey: "material-key",
            BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
            MaterialType: ResoniteMaterialType.Wireframe,
            TexturePath: null,
            TextureSourceKind: ResoniteTextureSourceKind.Bundled,
            Projection: ResoniteMaterialProjection.Uv,
            DepthOffset: null,
            SubmeshIndices: [0]);
    }

    private static ResoniteImportedMesh CreateTriangleMesh()
    {
        return new ResoniteImportedMesh(
            Vertices:
            [
                new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 1.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
            ],
            Submeshes:
            [
                new ResoniteMeshSubmesh(0, "material-key", [0, 1, 2]),
            ]);
    }

    private sealed class YOffsetFakeClient : IResoniteLinkClient
    {
        private readonly object gate = new();

        public Dictionary<string, Slot> SlotsById { get; } = [];
        public Dictionary<string, Component> ComponentsById { get; } = [];
        public List<string> ImportedTexturePaths { get; } = [];
        public List<ResoniteRawTextureImport> ImportedRawTextures { get; } = [];
        public List<ResoniteRawHdrTextureImport> ImportedRawHdrTextures { get; } = [];

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
            lock (gate)
            {
                ComponentsById[request.Data.ID] = request.Data;
            }

            return Task.CompletedTask;
        }

        public Task AddSlotAsync(AddSlot request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                SlotsById[request.Data.ID] = request.Data;
            }

            return Task.CompletedTask;
        }

        public async Task RunDataModelOperationBatchAsync(
            IReadOnlyList<DataModelOperation> operations,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            lock (gate)
            {
                ComponentsById.TryGetValue(componentId, out Component? component);
                return Task.FromResult(component);
            }
        }

        public Task<Slot?> GetSlotAsync(string slotId, int depth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                if (!SlotsById.TryGetValue(slotId, out Slot? slot))
                {
                    return Task.FromResult<Slot?>(null);
                }

                return Task.FromResult<Slot?>(CloneSlot(slot, depth));
            }
        }

        public Task<Uri> ImportMeshAsync(ImportMeshRawData request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new Uri("resdb:///mesh/0", UriKind.Absolute));
        }

        public Task<Uri> ImportTextureAsync(ResoniteTextureImport textureImport, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                switch (textureImport)
                {
                    case ResoniteFileTextureImport fileImport:
                        ImportedTexturePaths.Add(fileImport.AbsolutePath);
                        break;
                    case ResoniteRawTextureImport rawImport:
                        ImportedRawTextures.Add(rawImport);
                        if (rawImport.SourcePath is not null)
                        {
                            ImportedTexturePaths.Add(rawImport.SourcePath);
                        }

                        break;
                    case ResoniteRawHdrTextureImport rawHdrImport:
                        ImportedRawHdrTextures.Add(rawHdrImport);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported texture import type '{textureImport.GetType().Name}'.");
                }
            }

            return Task.FromResult(new Uri("resdb:///texture/0", UriKind.Absolute));
        }

        public Task UpdateComponentAsync(UpdateComponent request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        private Slot CloneSlot(Slot source, int depth)
        {
            Slot clone = new()
            {
                ID = source.ID,
                Parent = source.Parent,
                Name = source.Name,
                Position = source.Position,
                Components = source.Components,
            };

            if (depth <= 0)
            {
                return clone;
            }

            lock (gate)
            {
                clone.Children = SlotsById.Values
                    .Where(slot => string.Equals(slot.Parent?.TargetID, source.ID, StringComparison.Ordinal))
                    .Select(slot => CloneSlot(slot, depth - 1))
                    .ToList();
            }

            return clone;
        }
    }

    private sealed record ResoniteYTestScene(
        ResoniteConstructionMetadata Metadata,
        IReadOnlyList<ResoniteConstructionCityObject> CityObjects);
}
