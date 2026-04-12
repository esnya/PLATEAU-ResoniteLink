using Plateau.ResoniteLink.Cli;
using Plateau.ResoniteLink.Domain.Importing;

namespace Plateau.ResoniteLink.Tests.Cli;

public sealed class FixedCellCityObjectMeshBakerTests
{
    [Fact]
    public void TryBufferBakesLod1BuildingsInSameCellIntoSingleMesh()
    {
        FixedCellCityObjectMeshBaker baker = new(cellSizeMeters: 64.0, maxCityObjectsPerBatch: 2, maxVerticesPerBatch: 1000);
        ResoniteConstructionCityObject first = CreateTriangleBuilding("first", x: 10.0, z: 12.0, actualMeshCode: "533945");
        ResoniteConstructionCityObject second = CreateTriangleBuilding("second", x: 18.0, z: 20.0, actualMeshCode: "533945");

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
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("left", x: 10.0, z: 10.0, actualMeshCode: "533945"), out _));
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("right", x: 80.0, z: 10.0, actualMeshCode: "533945"), out _));

        IReadOnlyList<ResoniteConstructionCityObject> baked = baker.FlushAll();

        Assert.Equal(2, baked.Count);
        Assert.Contains(baked, static cityObject => cityObject.Transform.Position == new ResoniteFloat3(0.0, 0.0, 0.0));
        Assert.Contains(baked, static cityObject => cityObject.Transform.Position == new ResoniteFloat3(64.0, 0.0, 0.0));
    }

    [Fact]
    public void TryBufferFlushesOldestSparseCellWhenBufferedCellLimitIsExceeded()
    {
        FixedCellCityObjectMeshBaker baker = new(
            cellSizeMeters: 64.0,
            maxCityObjectsPerBatch: 10,
            maxVerticesPerBatch: 1000,
            maxBufferedCells: 2);
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("left", x: 10.0, z: 10.0, actualMeshCode: "53394525"), out ResoniteConstructionCityObject? firstBaked));
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("center", x: 80.0, z: 10.0, actualMeshCode: "53394526"), out ResoniteConstructionCityObject? secondBaked));

        bool thirdBuffered = baker.TryBuffer(CreateTriangleBuilding("right", x: 138.0, z: 10.0, actualMeshCode: "53394527"), out ResoniteConstructionCityObject? flushed);

        Assert.True(thirdBuffered);
        Assert.Null(firstBaked);
        Assert.Null(secondBaked);
        Assert.NotNull(flushed);
        Assert.Equal(new ResoniteFloat3(0.0, 0.0, 0.0), flushed.Transform.Position);
        Assert.Equal("53394525", flushed.ActualMeshCode);

        IReadOnlyList<ResoniteConstructionCityObject> remainingBatches = baker.FlushAll();
        Assert.Equal(2, remainingBatches.Count);
        Assert.Contains(remainingBatches, static cityObject => cityObject.ActualMeshCode == "53394526");
        Assert.Contains(remainingBatches, static cityObject => cityObject.ActualMeshCode == "53394527");
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

    [Fact]
    public void FlushAllReturnsSingleBatchPerEightDigitMeshCodeAcrossCells()
    {
        FixedCellCityObjectMeshBaker baker = new(cellSizeMeters: 64.0, maxCityObjectsPerBatch: 10, maxVerticesPerBatch: 1000);
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("left", x: 10.0, z: 10.0), out _));
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("right", x: 80.0, z: 10.0), out _));

        IReadOnlyList<ResoniteConstructionCityObject> baked = baker.FlushAll();

        ResoniteConstructionCityObject cityObject = Assert.Single(baked);
        Assert.Equal("53394525", cityObject.ActualMeshCode);
        Assert.Equal(new ResoniteFloat3(0.0, 0.0, 0.0), cityObject.Transform.Position);
        Assert.Equal(6, cityObject.Mesh.Vertices.Count);
    }

    [Fact]
    public void TryBufferFlushesWhenEightDigitMeshBakeHitsBatchLimits()
    {
        FixedCellCityObjectMeshBaker baker = new(cellSizeMeters: 64.0, maxCityObjectsPerBatch: 1, maxVerticesPerBatch: 3);
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("first", x: 10.0, z: 10.0), out ResoniteConstructionCityObject? firstBaked));
        Assert.True(baker.TryBuffer(CreateTriangleBuilding("second", x: 80.0, z: 10.0), out ResoniteConstructionCityObject? secondBaked));

        Assert.NotNull(firstBaked);
        Assert.NotNull(secondBaked);
        Assert.Equal("53394525", firstBaked.ActualMeshCode);
        Assert.Equal("53394525", secondBaked.ActualMeshCode);
        Assert.Equal(new ResoniteFloat3(0.0, 0.0, 0.0), firstBaked.Transform.Position);
        Assert.Equal(new ResoniteFloat3(0.0, 0.0, 0.0), secondBaked.Transform.Position);
        Assert.Equal(3, firstBaked.Mesh.Vertices.Count);
        Assert.Equal(3, secondBaked.Mesh.Vertices.Count);

        Assert.Empty(baker.FlushAll());
    }

    private static ResoniteConstructionCityObject CreateTriangleBuilding(string slotKey, double x, double z, string actualMeshCode = "53394525")
    {
        return new ResoniteConstructionCityObject(
            SlotKey: slotKey,
            DisplayName: slotKey,
            PackageName: "bldg",
            ActualMeshCode: actualMeshCode,
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
