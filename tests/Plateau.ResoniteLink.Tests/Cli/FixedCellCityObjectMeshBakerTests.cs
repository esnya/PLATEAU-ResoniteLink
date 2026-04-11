using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class FixedCellCityObjectMeshBakerTests
{
    [Fact]
    public void TryBufferBakesLod1BuildingsInSameCellIntoSingleMesh()
    {
        FixedCellCityObjectMeshBaker baker = new(cellSizeMeters: 64.0, maxCityObjectsPerBatch: 2, maxVerticesPerBatch: 1000);
        ResoniteConstructionCityObject first = CreateTriangleBuilding("first", x: 10.0, z: 12.0);
        ResoniteConstructionCityObject second = CreateTriangleBuilding("second", x: 18.0, z: 20.0);

        bool firstBuffered = baker.TryBuffer(first, out ResoniteConstructionCityObject? firstBaked);
        bool secondBuffered = baker.TryBuffer(second, out ResoniteConstructionCityObject? baked);

        Assert.True(firstBuffered);
        Assert.Null(firstBaked);
        Assert.True(secondBuffered);
        Assert.NotNull(baked);
        Assert.Equal("bldg", baked.PackageName);
        Assert.Equal(1, baked.LodLevel);
        Assert.Equal(6, baked.Mesh.Vertices.Count);
        Assert.Single(baked.Mesh.Submeshes);
        Assert.Single(baked.Materials);
        Assert.Equal([0], baked.Materials[0].SubmeshIndices);
        Assert.Equal(new ResoniteFloat3(0.0, 0.0, 0.0), baked.Transform.Position);

        ResoniteFloat3[] positions = baked.Mesh.Vertices.Select(static vertex => vertex.Position).ToArray();
        Assert.Contains(new ResoniteFloat3(10.0, 0.0, 12.0), positions);
        Assert.Contains(new ResoniteFloat3(11.0, 0.0, 12.0), positions);
        Assert.Contains(new ResoniteFloat3(10.0, 0.0, 13.0), positions);
        Assert.Contains(new ResoniteFloat3(18.0, 0.0, 20.0), positions);
        Assert.Contains(new ResoniteFloat3(19.0, 0.0, 20.0), positions);
        Assert.Contains(new ResoniteFloat3(18.0, 0.0, 21.0), positions);
    }

    [Fact]
    public void FlushAllReturnsSeparateBatchesPerCell()
    {
        FixedCellCityObjectMeshBaker baker = new(cellSizeMeters: 64.0, maxCityObjectsPerBatch: 10, maxVerticesPerBatch: 1000);
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("left", x: 10.0, z: 10.0), out _));
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("right", x: 80.0, z: 10.0), out _));

        IReadOnlyList<ResoniteConstructionCityObject> baked = baker.FlushAll();

        Assert.Equal(2, baked.Count);
        Assert.Contains(baked, static cityObject => cityObject.Transform.Position == new ResoniteFloat3(0.0, 0.0, 0.0));
        Assert.Contains(baked, static cityObject => cityObject.Transform.Position == new ResoniteFloat3(64.0, 0.0, 0.0));
    }

    [Fact]
    public void TryBufferSkipsNonTargetCityObjects()
    {
        FixedCellCityObjectMeshBaker baker = new(cellSizeMeters: 64.0, maxCityObjectsPerBatch: 2, maxVerticesPerBatch: 1000);
        ResoniteConstructionCityObject lod2Building = CreateTriangleBuilding("lod2", x: 10.0, z: 10.0) with { LodLevel = 2 };

        bool buffered = baker.TryBuffer(lod2Building, out ResoniteConstructionCityObject? baked);

        Assert.False(buffered);
        Assert.Null(baked);
        Assert.Empty(baker.FlushAll());
    }

    private static ResoniteConstructionCityObject CreateTriangleBuilding(string slotKey, double x, double z)
    {
        return new ResoniteConstructionCityObject(
            SlotKey: slotKey,
            DisplayName: slotKey,
            PackageName: "bldg",
            ActualMeshCode: "53394525",
            LodLevel: 1,
            Transform: new ResoniteTransform(new ResoniteFloat3(x, 0.0, z)),
            Mesh: new ResoniteImportedMesh(
                [
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(1.0, 0.0, 0.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(1.0, 0.0)),
                    new ResoniteMeshVertex(new ResoniteFloat3(0.0, 0.0, 1.0), new ResoniteFloat3(0.0, 1.0, 0.0), new ResoniteFloat2(0.0, 1.0)),
                ],
                [
                    new ResoniteMeshSubmesh(
                        Index: 0,
                        MaterialKey: "shared-material",
                        TriangleVertexIndices: [0, 1, 2]),
                ]),
            Materials:
            [
                new ResoniteMaterialBinding(
                    MaterialKey: "shared-material",
                    BaseColor: new ResoniteColor(1.0, 1.0, 1.0, 1.0),
                    MaterialType: ResoniteMaterialType.Standard,
                    TexturePath: null,
                    TextureSourceKind: ResoniteTextureSourceKind.Bundled,
                    Projection: ResoniteMaterialProjection.Uv,
                    DepthOffset: null,
                    SubmeshIndices: [0]),
            ]);
    }
}
