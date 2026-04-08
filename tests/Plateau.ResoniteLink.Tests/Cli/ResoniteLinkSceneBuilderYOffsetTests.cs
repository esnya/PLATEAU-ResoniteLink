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

        await builder.BeginAsync(scene.Metadata, "runtime/resonite");
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

    private static ResoniteYTestScene CreateScene(
        string dataset,
        string meshCode,
        string sourceObjectKey,
        double height,
        string fixturePath)
    {
        ResoniteConstructionMetadata metadata = new(
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
                TerrainTextureOverlays: []),
            Attribution: new ResoniteAttribution(
                DatasetLicense: new ResoniteLicenseComponentMetadata(
                    RequireCredit: true,
                    CreditText: "PLATEAU Open Data Terms",
                    LicenseName: "PLATEAU Open Data Terms",
                    LicenseUrl: "https://www.mlit.go.jp/plateau/site-policy/"),
                MaterialLicenses: []),
            LocalOrigin: new ResoniteLocalOrigin(35.6875, 139.69375, 0.0));

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

        public Task<Uri> ImportTextureAsync(string filePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
